import asyncio
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
import threading
from pathlib import Path
from typing import Any

from dotenv import load_dotenv
from fastapi import FastAPI, File, Form, HTTPException, Query, UploadFile
from openai import OpenAI
import edge_tts
from faster_whisper import WhisperModel
from pydantic import BaseModel, Field
from fastapi.responses import Response


BACKEND_DIR = Path(__file__).resolve().parent
load_dotenv(dotenv_path=BACKEND_DIR / ".env")

app = FastAPI(
    title="VR智能AI面试后端",
    description="Unity VR智能AI面试项目",
    version="5.2.0",
)


DEEPSEEK_API_KEY = os.getenv(
    "DEEPSEEK_API_KEY",
    "",
).strip()

DEEPSEEK_MODEL = os.getenv(
    "DEEPSEEK_MODEL",
    "deepseek-v4-flash",
).strip()


WHISPER_MODEL_NAME = os.getenv(
    "WHISPER_MODEL",
    "small",
).strip()

WHISPER_DEVICE = os.getenv(
    "WHISPER_DEVICE",
    "cpu",
).strip()

WHISPER_COMPUTE_TYPE = os.getenv(
    "WHISPER_COMPUTE_TYPE",
    "int8",
).strip()


TTS_DEFAULT_VOICE = os.getenv(
    "TTS_VOICE",
    "zh-CN-YunyangNeural",
).strip()

TTS_DEFAULT_RATE = os.getenv(
    "TTS_RATE",
    "-8%",
).strip()

TTS_DEFAULT_VOLUME = os.getenv(
    "TTS_VOLUME",
    "+0%",
).strip()

TTS_DEFAULT_PITCH = os.getenv(
    "TTS_PITCH",
    "-4Hz",
).strip()


TTS_CACHE_DIR = Path(
    os.getenv(
        "TTS_CACHE_DIR",
        str(BACKEND_DIR / ".cache" / "vr_interview_tts_cache"),
    )
).expanduser()

TTS_CACHE_DIR.mkdir(
    parents=True,
    exist_ok=True,
)


try:
    TTS_MAX_ATTEMPTS = max(
        1,
        min(
            4,
            int(os.getenv("TTS_MAX_ATTEMPTS", "2")),
        ),
    )
except ValueError:
    TTS_MAX_ATTEMPTS = 2

try:
    TTS_ATTEMPT_TIMEOUT_SECONDS = max(
        2.0,
        float(
            os.getenv(
                "TTS_ATTEMPT_TIMEOUT_SECONDS",
                "12",
            )
        ),
    )
except ValueError:
    TTS_ATTEMPT_TIMEOUT_SECONDS = 12.0

try:
    TTS_RETRY_DELAY_SECONDS = max(
        0.0,
        float(
            os.getenv(
                "TTS_RETRY_DELAY_SECONDS",
                "0.4",
            )
        ),
    )
except ValueError:
    TTS_RETRY_DELAY_SECONDS = 0.4

try:
    TTS_FAILURE_COOLDOWN_SECONDS = max(
        0.0,
        float(
            os.getenv(
                "TTS_FAILURE_COOLDOWN_SECONDS",
                "20",
            )
        ),
    )
except ValueError:
    TTS_FAILURE_COOLDOWN_SECONDS = 20.0

TTS_FALLBACK_VOICES = [
    voice_name.strip()
    for voice_name in os.getenv(
        "TTS_FALLBACK_VOICES",
        (
            "zh-CN-YunyangNeural,"
            "zh-CN-YunjianNeural"
        ),
    ).split(",")
    if voice_name.strip()
]

tts_generation_semaphore = asyncio.Semaphore(1)
tts_circuit_open_until = 0.0

try:
    MAX_TTS_TEXT_LENGTH = max(
        20,
        int(os.getenv("MAX_TTS_TEXT_LENGTH", "500")),
    )
except ValueError:
    MAX_TTS_TEXT_LENGTH = 500


TTS_PERCENT_PATTERN = re.compile(
    r"^[+-]\d{1,3}%$"
)

TTS_PITCH_PATTERN = re.compile(
    r"^[+-]\d{1,3}Hz$"
)

WHISPER_DOWNLOAD_ROOT = os.getenv(
    "WHISPER_DOWNLOAD_ROOT",
    "",
).strip()

try:
    WHISPER_CPU_THREADS = max(
        1,
        int(os.getenv("WHISPER_CPU_THREADS", "4")),
    )
except ValueError:
    WHISPER_CPU_THREADS = 4

try:
    MAX_AUDIO_FILE_BYTES = max(
        1,
        int(os.getenv("MAX_AUDIO_FILE_MB", "25")),
    ) * 1024 * 1024
except ValueError:
    MAX_AUDIO_FILE_BYTES = 25 * 1024 * 1024


whisper_model: WhisperModel | None = None
whisper_model_load_lock = threading.Lock()
whisper_transcribe_lock = threading.Lock()


client: OpenAI | None = None

if DEEPSEEK_API_KEY:
    client = OpenAI(
        api_key=DEEPSEEK_API_KEY,
        base_url="https://api.deepseek.com",
    )


MAX_QUESTION_COUNT = 4


class InterviewRequest(BaseModel):
    target_position: str = "软件开发工程师"

    questions: list[str] = Field(
        default_factory=list
    )

    answers: list[str]


class InterviewScoreResponse(BaseModel):
    overall_score: int
    language_score: int
    reaction_score: int
    professional_score: int
    demeanor_score: int

    highlights: str
    suggestions: str


class NextQuestionRequest(BaseModel):
    question_number: int = Field(
        ge=1,
        le=MAX_QUESTION_COUNT,
    )

    target_position: str = "软件开发工程师"

    previous_questions: list[str] = Field(
        default_factory=list
    )

    previous_answers: list[str] = Field(
        default_factory=list
    )


class NextQuestionResponse(BaseModel):
    reaction: str = ""
    question: str
    speech_text: str
    is_follow_up: bool = False


class SpeechToTextResponse(BaseModel):
    text: str
    language: str
    language_probability: float
    duration: float


DEFAULT_INTERVIEW_QUESTIONS = [
    "请简单介绍一下自己，并说明你为什么对软件开发岗位感兴趣。",

    "请介绍一个你参与过的项目，并说明你在项目中负责的工作。",

    "你在项目中遇到过什么困难？你最终是怎样解决的？",

    "如果团队成员对项目方案存在分歧，你会怎样处理？",
]


def ensure_client() -> OpenAI:
    if client is None:
        raise HTTPException(
            status_code=500,
            detail="未检测到DeepSeek API Key，请检查.env文件。",
        )

    return client


def clamp_score(value: Any) -> int:
    try:
        score = int(round(float(value)))
    except (TypeError, ValueError):
        score = 60

    return max(0, min(score, 100))


def parse_json_content(
    content: str,
) -> dict[str, Any]:
    cleaned = content.strip()

    if cleaned.startswith("```"):
        cleaned = cleaned.replace(
            "```json",
            "",
            1,
        )

        cleaned = cleaned.replace(
            "```JSON",
            "",
            1,
        )

        cleaned = cleaned.replace(
            "```",
            "",
        )

        cleaned = cleaned.strip()

    start_index = cleaned.find("{")
    end_index = cleaned.rfind("}")

    if start_index == -1 or end_index == -1:
        raise ValueError("模型没有返回有效JSON。")

    json_text = cleaned[
        start_index:end_index + 1
    ]

    result = json.loads(json_text)

    if not isinstance(result, dict):
        raise ValueError("模型返回的不是JSON对象。")

    return result


def is_answer_too_short(
    answer: str,
) -> bool:
    compact_answer = "".join(
        answer.strip().split()
    )

    return len(compact_answer) < 8


def build_history_text(
    questions: list[str],
    answers: list[str],
) -> str:
    if not questions:
        return "目前没有历史问题，这是面试的第一道题。"

    sections: list[str] = []

    for index, question in enumerate(questions):
        answer = ""

        if index < len(answers):
            answer = answers[index]

        sections.append(
            f"第{index + 1}题：{question}\n"
            f"候选人回答：{answer}"
        )

    return "\n\n".join(sections)


def build_interview_text(
    questions: list[str],
    answers: list[str],
) -> str:
    sections: list[str] = []

    for index, answer in enumerate(answers):
        if index < len(questions):
            question = questions[index]
        else:
            question = DEFAULT_INTERVIEW_QUESTIONS[index]

        sections.append(
            f"第{index + 1}题：{question}\n"
            f"候选人回答：{answer.strip()}"
        )

    return "\n\n".join(sections)


def get_fallback_question(
    question_number: int,
) -> str:
    index = question_number - 1

    if 0 <= index < len(
        DEFAULT_INTERVIEW_QUESTIONS
    ):
        return DEFAULT_INTERVIEW_QUESTIONS[index]

    return "请再举一个具体例子，说明你的相关能力。"


def ensure_sentence_end(
    text: str,
) -> str:
    cleaned = text.strip()

    if not cleaned:
        return ""

    if cleaned[-1] not in "。！？!?":
        cleaned += "。"

    return cleaned


def build_speech_text(
    reaction: str,
    question: str,
) -> str:
    reaction_text = ensure_sentence_end(
        reaction
    )

    question_text = question.strip()

    if not question_text:
        return reaction_text

    if reaction_text:
        return (
            reaction_text
            + question_text
        )

    return question_text


def get_fallback_turn(
    question_number: int,
    target_position: str,
    previous_answers: list[str],
) -> NextQuestionResponse:
    last_answer = (
        previous_answers[-1].strip()
        if previous_answers
        else ""
    )

    if question_number == 1:
        reaction = (
            f"你好，欢迎参加“{target_position}”"
            "岗位的模拟面试。"
            "我们先从简单的问题开始。"
        )
        question = get_fallback_question(
            question_number
        )
        is_follow_up = False

    elif is_answer_too_short(last_answer):
        reaction = (
            "我还想进一步了解你刚才提到的内容。"
        )
        question = (
            "能结合一个具体事例详细说明吗？"
        )
        is_follow_up = True

    else:
        reaction = "好的，我了解了。"
        question = get_fallback_question(
            question_number
        )
        is_follow_up = False

    return NextQuestionResponse(
        reaction=reaction,
        question=question,
        speech_text=build_speech_text(
            reaction,
            question,
        ),
        is_follow_up=is_follow_up,
    )


def clean_feedback_item(
    value: Any,
) -> str:
    text = str(value or "").strip()

    text = re.sub(
        r"^\s*(?:[-•·]|\d+[.、）)])\s*",
        "",
        text,
    )

    return text.strip()


def format_feedback_section(
    value: Any,
    fallback_items: list[str],
    maximum_items: int = 4,
) -> str:
    items: list[str] = []

    if isinstance(value, list):
        raw_items = value
    elif isinstance(value, str):
        raw_items = re.split(
            r"(?:\r?\n)+|(?=\d+[.、）)])",
            value,
        )
    else:
        raw_items = []

    for raw_item in raw_items:
        item = clean_feedback_item(
            raw_item
        )

        if (
            item
            and item not in items
        ):
            items.append(item)

        if len(items) >= maximum_items:
            break

    for fallback_item in fallback_items:
        if len(items) >= 3:
            break

        cleaned_fallback = clean_feedback_item(
            fallback_item
        )

        if (
            cleaned_fallback
            and cleaned_fallback not in items
        ):
            items.append(cleaned_fallback)

    return "\n".join(
        f"{index + 1}. {item}"
        for index, item in enumerate(items)
    )


def build_fallback_feedback(
    target_position: str,
    questions: list[str],
    answers: list[str],
) -> tuple[str, str]:
    substantial_answers = [
        answer
        for answer in answers
        if len("".join(answer.split())) >= 30
    ]

    first_specific_answer = next(
        (
            answer
            for answer in substantial_answers
        ),
        answers[0] if answers else "",
    )

    answer_excerpt = (
        first_specific_answer[:45]
        + (
            "……"
            if len(first_specific_answer) > 45
            else ""
        )
    )

    highlights = [
        (
            "能够完成全部面试问题，"
            "整体配合度和回答连续性较稳定。"
        ),
        (
            f"回答中提到了“{answer_excerpt}”，"
            "说明候选人能够提供一定的经历或任务信息。"
            if answer_excerpt
            else (
                "能够围绕题目给出基本回应，"
                "具备初步的岗位表达意识。"
            )
        ),
        (
            f"部分回答能够围绕“{target_position}”岗位展开，"
            "体现了基本的求职目标和岗位关注。"
        ),
    ]

    suggestions = [
        (
            "建议使用STAR结构展开经历，"
            "分别说明情境、任务、行动和结果，"
            "避免只描述做过什么。"
        ),
        (
            f"围绕“{target_position}”岗位补充更具体的技能、"
            "工具、个人职责和解决问题过程。"
        ),
        (
            "增加量化结果，例如完成数量、准确率、"
            "效率提升、用户反馈或项目最终采用情况。"
        ),
    ]

    return (
        format_feedback_section(
            highlights,
            highlights,
        ),
        format_feedback_section(
            suggestions,
            suggestions,
        ),
    )


def calculate_fallback_score(
    target_position: str,
    questions: list[str],
    answers: list[str],
) -> InterviewScoreResponse:
    answer_lengths = [
        len("".join(answer.split()))
        for answer in answers
    ]

    average_length = 0

    if answer_lengths:
        average_length = (
            sum(answer_lengths)
            // len(answer_lengths)
        )

    substantial_count = sum(
        1
        for length in answer_lengths
        if length >= 30
    )

    language_score = clamp_score(
        50 + average_length // 3
    )

    reaction_score = clamp_score(
        50 + substantial_count * 8
    )

    professional_score = clamp_score(
        48 + substantial_count * 9
    )

    demeanor_score = 80

    overall_score = round(
        language_score * 0.30
        + reaction_score * 0.20
        + professional_score * 0.40
        + demeanor_score * 0.10
    )

    (
        fallback_highlights,
        fallback_suggestions,
    ) = build_fallback_feedback(
        target_position,
        questions,
        answers,
    )

    return InterviewScoreResponse(
        overall_score=overall_score,
        language_score=language_score,
        reaction_score=reaction_score,
        professional_score=professional_score,
        demeanor_score=demeanor_score,
        highlights=fallback_highlights,
        suggestions=fallback_suggestions,
    )


def get_whisper_model() -> WhisperModel:
    global whisper_model

    if whisper_model is not None:
        return whisper_model

    with whisper_model_load_lock:
        if whisper_model is not None:
            return whisper_model

        model_kwargs: dict[str, Any] = {
            "device": WHISPER_DEVICE,
            "compute_type": WHISPER_COMPUTE_TYPE,
            "cpu_threads": WHISPER_CPU_THREADS,
        }

        if WHISPER_DOWNLOAD_ROOT:
            model_kwargs["download_root"] = (
                WHISPER_DOWNLOAD_ROOT
            )

        print(
            "正在加载语音识别模型："
            f"{WHISPER_MODEL_NAME}，"
            f"设备：{WHISPER_DEVICE}，"
            f"计算类型：{WHISPER_COMPUTE_TYPE}"
        )

        whisper_model = WhisperModel(
            WHISPER_MODEL_NAME,
            **model_kwargs,
        )

        print("语音识别模型加载完成。")

    return whisper_model


def transcribe_audio_file(
    audio_path: str,
    input_type: str,
) -> SpeechToTextResponse:
    model = get_whisper_model()

    normalized_input_type = (
        input_type.strip().lower()
    )

    if normalized_input_type == "target_position":
        initial_prompt = (
            "以下是一段中文求职岗位名称，"
            "例如数据分析师、软件开发工程师、"
            "产品经理、新媒体运营、教师。"
            "请准确识别说话人所说的岗位名称。"
        )
        beam_size = 3
    else:
        normalized_input_type = "interview_answer"
        initial_prompt = (
            "以下是一段中文求职面试回答，"
            "请准确识别人名、专业术语、"
            "英文缩写和数字。"
        )
        beam_size = 5

    with whisper_transcribe_lock:
        segments, info = model.transcribe(
            audio_path,
            language="zh",
            task="transcribe",
            beam_size=beam_size,
            vad_filter=True,
            vad_parameters={
                "min_silence_duration_ms": 500,
            },
            condition_on_previous_text=False,
            initial_prompt=initial_prompt,
        )

        text = "".join(
            segment.text
            for segment in segments
        ).strip()

    if not text:
        raise ValueError(
            "没有识别到有效语音，请靠近麦克风后重试。"
        )

    language = str(
        getattr(info, "language", "zh") or "zh"
    )

    language_probability = float(
        getattr(
            info,
            "language_probability",
            0.0,
        ) or 0.0
    )

    duration = float(
        getattr(info, "duration", 0.0) or 0.0
    )

    return SpeechToTextResponse(
        text=text,
        language=language,
        language_probability=language_probability,
        duration=duration,
    )


@app.get("/")
def home() -> dict[str, str]:
    return {
        "message": "Unity智能面试后端正在运行"
    }


@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "status": "ok",
        "deepseek_configured": bool(
            DEEPSEEK_API_KEY
        ),
        "model": DEEPSEEK_MODEL,
        "speech_to_text_enabled": True,
        "whisper_model": WHISPER_MODEL_NAME,
        "whisper_device": WHISPER_DEVICE,
        "whisper_compute_type": (
            WHISPER_COMPUTE_TYPE
        ),
        "whisper_model_loaded": (
            whisper_model is not None
        ),
        "text_to_speech_enabled": True,
        "tts_default_voice": TTS_DEFAULT_VOICE,
        "tts_max_attempts": TTS_MAX_ATTEMPTS,
        "tts_attempt_timeout_seconds": (
            TTS_ATTEMPT_TIMEOUT_SECONDS
        ),
        "tts_failure_cooldown_seconds": (
            TTS_FAILURE_COOLDOWN_SECONDS
        ),
        "tts_circuit_open": (
            tts_circuit_open_until
            > time.monotonic()
        ),
    }


def build_tts_voice_candidates(
    requested_voice: str,
) -> list[str]:
    candidates: list[str] = []

    for voice_name in [
        requested_voice,
        TTS_DEFAULT_VOICE,
        *TTS_FALLBACK_VOICES,
    ]:
        cleaned_voice = voice_name.strip()

        if (
            cleaned_voice
            and cleaned_voice not in candidates
        ):
            candidates.append(cleaned_voice)

    return candidates


async def collect_edge_tts_audio(
    text: str,
    voice: str,
    rate: str,
    volume: str,
    pitch: str,
) -> bytes:
    communicator = edge_tts.Communicate(
        text=text,
        voice=voice,
        rate=rate,
        volume=volume,
        pitch=pitch,
    )

    audio_chunks: list[bytes] = []

    async for chunk in communicator.stream():
        if chunk.get("type") != "audio":
            continue

        audio_data = chunk.get("data")

        if isinstance(
            audio_data,
            (bytes, bytearray),
        ):
            audio_chunks.append(
                bytes(audio_data)
            )

    audio_bytes = b"".join(audio_chunks)

    if not audio_bytes:
        raise ValueError(
            "语音服务没有返回有效音频。"
        )

    return audio_bytes


def generate_windows_sapi_wav_sync(
    text: str,
    voice: str,
    rate: str,
    volume: str,
) -> bytes:
    rate_percent = int(rate[:-1])
    volume_percent = int(volume[:-1])
    sapi_rate = max(-10, min(10, round(rate_percent / 10)))
    sapi_volume = max(0, min(100, 100 + volume_percent))

    def escape_powershell(value: str) -> str:
        return value.replace("'", "''")

    temporary_file = tempfile.NamedTemporaryFile(
        dir=TTS_CACHE_DIR,
        suffix=".wav",
        delete=False,
    )
    output_path = Path(temporary_file.name)
    temporary_file.close()

    try:
        output_path.unlink(missing_ok=True)

        script = (
            "Add-Type -AssemblyName System.Speech;"
            "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;"
            "$v=$s.GetInstalledVoices() | "
            "Where-Object { $_.Enabled -and "
            "$_.VoiceInfo.Name -eq 'Microsoft Kangkang' } | "
            "Select-Object -First 1;"
            "if($null -eq $v){$v=$s.GetInstalledVoices() | "
            "Where-Object { $_.Enabled -and "
            "$_.VoiceInfo.Culture.Name -eq 'zh-CN' -and "
            "$_.VoiceInfo.Gender -eq 'Male' } | "
            "Select-Object -First 1};"
            "if($null -eq $v){$v=$s.GetInstalledVoices() | "
            "Where-Object { $_.Enabled -and "
            "$_.VoiceInfo.Culture.Name -eq 'zh-CN' } | "
            "Select-Object -First 1};"
            "if($null -ne $v){$s.SelectVoice($v.VoiceInfo.Name)};"
            f"$s.Rate={sapi_rate};"
            f"$s.Volume={sapi_volume};"
            "$s.SetOutputToWaveFile('"
            + escape_powershell(str(output_path))
            + "');"
            "$s.Speak('"
            + escape_powershell(text)
            + "');"
            "$s.Dispose();"
        )

        completed = subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script,
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            timeout=25,
            check=False,
            creationflags=0x08000000,
        )

        if completed.returncode != 0:
            error_text = completed.stderr.decode(
                errors="replace"
            ).strip()
            raise RuntimeError(
                "Windows 中文语音生成失败："
                + (
                    error_text
                    if error_text
                    else f"退出码 {completed.returncode}"
                )
            )

        audio_bytes = output_path.read_bytes()

        if (
            len(audio_bytes) <= 44
            or audio_bytes[:4] != b"RIFF"
            or audio_bytes[8:12] != b"WAVE"
        ):
            raise RuntimeError(
                "Windows 中文语音没有生成有效 WAV 音频。"
            )

        return audio_bytes
    finally:
        output_path.unlink(missing_ok=True)


async def generate_windows_sapi_wav(
    text: str,
    voice: str,
    rate: str,
    volume: str,
) -> bytes:
    return await asyncio.to_thread(
        generate_windows_sapi_wav_sync,
        text,
        voice,
        rate,
        volume,
    )


async def generate_tts_with_retry(
    text: str,
    requested_voice: str,
    rate: str,
    volume: str,
    pitch: str,
) -> tuple[bytes, str, int]:
    global tts_circuit_open_until

    voice_candidates = build_tts_voice_candidates(
        requested_voice
    )

    last_error: Exception | None = None

    async with tts_generation_semaphore:
        for attempt_index in range(
            TTS_MAX_ATTEMPTS
        ):
            voice_index = min(
                attempt_index,
                len(voice_candidates) - 1,
            )

            current_voice = voice_candidates[
                voice_index
            ]

            try:
                audio_bytes = await asyncio.wait_for(
                    collect_edge_tts_audio(
                        text=text,
                        voice=current_voice,
                        rate=rate,
                        volume=volume,
                        pitch=pitch,
                    ),
                    timeout=(
                        TTS_ATTEMPT_TIMEOUT_SECONDS
                    ),
                )

                tts_circuit_open_until = 0.0

                return (
                    audio_bytes,
                    current_voice,
                    attempt_index + 1,
                )

            except Exception as error:
                last_error = error

                print(
                    "TTS生成尝试失败："
                    f"第{attempt_index + 1}次，"
                    f"声音={current_voice}，"
                    f"错误={repr(error)}"
                )

                if (
                    attempt_index
                    < TTS_MAX_ATTEMPTS - 1
                    and TTS_RETRY_DELAY_SECONDS > 0
                ):
                    await asyncio.sleep(
                        TTS_RETRY_DELAY_SECONDS
                    )

    tts_circuit_open_until = (
        time.monotonic()
        + TTS_FAILURE_COOLDOWN_SECONDS
    )

    raise RuntimeError(
        "多次尝试生成语音仍然失败："
        f"{str(last_error)}"
    )


@app.get("/text-to-speech")
async def text_to_speech(
    text: str = Query(
        ...,
        min_length=1,
        max_length=MAX_TTS_TEXT_LENGTH,
    ),
    voice: str = Query(
        default=TTS_DEFAULT_VOICE,
        min_length=1,
        max_length=100,
    ),
    rate: str = Query(
        default=TTS_DEFAULT_RATE,
        min_length=2,
        max_length=10,
    ),
    volume: str = Query(
        default=TTS_DEFAULT_VOLUME,
        min_length=2,
        max_length=10,
    ),
    pitch: str = Query(
        default=TTS_DEFAULT_PITCH,
        min_length=3,
        max_length=12,
    ),
    audio_format: str = Query(
        default="mp3",
        alias="format",
        min_length=3,
        max_length=3,
    ),
) -> Response:
    global tts_circuit_open_until

    cleaned_text = " ".join(
        text.strip().split()
    )

    if not cleaned_text:
        raise HTTPException(
            status_code=400,
            detail="朗读文本不能为空。",
        )

    if not TTS_PERCENT_PATTERN.fullmatch(rate):
        raise HTTPException(
            status_code=400,
            detail="语速格式错误，应类似 +0%、-10% 或 +15%。",
        )

    if not TTS_PERCENT_PATTERN.fullmatch(volume):
        raise HTTPException(
            status_code=400,
            detail="音量格式错误，应类似 +0%、-10% 或 +10%。",
        )

    if not TTS_PITCH_PATTERN.fullmatch(pitch):
        raise HTTPException(
            status_code=400,
            detail="音调格式错误，应类似 +0Hz、-10Hz 或 +10Hz。",
        )

    cleaned_audio_format = audio_format.strip().lower()

    if cleaned_audio_format not in {"mp3", "wav"}:
        raise HTTPException(
            status_code=400,
            detail="语音格式仅支持 mp3 或 wav。",
        )

    cache_source = (
        cleaned_text
        + "\n"
        + voice
        + "\n"
        + rate
        + "\n"
        + volume
        + "\n"
        + pitch
        + "\n"
        + cleaned_audio_format
    )

    cache_key = hashlib.sha256(
        cache_source.encode("utf-8")
    ).hexdigest()

    cache_path = (
        TTS_CACHE_DIR
        / f"{cache_key}.{cleaned_audio_format}"
    )

    media_type = (
        "audio/wav"
        if cleaned_audio_format == "wav"
        else "audio/mpeg"
    )

    if cache_path.exists():
        try:
            cached_audio = cache_path.read_bytes()

            if cached_audio:
                return Response(
                    content=cached_audio,
                    media_type=media_type,
                    headers={
                        "Cache-Control": (
                            "public, max-age=86400"
                        ),
                        "X-TTS-Cache": "HIT",
                    },
                )
        except OSError:
            pass

    if cleaned_audio_format == "wav":
        try:
            async with tts_generation_semaphore:
                audio_bytes = await asyncio.wait_for(
                    generate_windows_sapi_wav(
                        text=cleaned_text,
                        voice=voice,
                        rate=rate,
                        volume=volume,
                    ),
                    timeout=30,
                )

            try:
                cache_path.write_bytes(audio_bytes)
            except OSError:
                pass

            print(
                "已使用 Windows 中文语音生成面试官 WAV："
                f"{cleaned_text[:50]}"
            )

            return Response(
                content=audio_bytes,
                media_type=media_type,
                headers={
                    "Cache-Control": "public, max-age=86400",
                    "X-TTS-Cache": "MISS",
                    "X-TTS-Provider": "windows-sapi",
                },
            )
        except Exception as error:
            print(
                "Windows 中文语音生成失败：",
                repr(error),
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    "本机中文语音生成失败："
                    f"{str(error)}"
                ),
            ) from error

    remaining_cooldown = (
        tts_circuit_open_until
        - time.monotonic()
    )

    if remaining_cooldown > 0:
        raise HTTPException(
            status_code=503,
            detail=(
                "语音服务刚刚连续失败，"
                "当前已快速回退到文字模式。"
                f"约{remaining_cooldown:.0f}秒后再尝试语音。"
            ),
            headers={
                "Retry-After": str(
                    max(
                        1,
                        int(remaining_cooldown),
                    )
                )
            },
        )

    try:
        (
            audio_bytes,
            used_voice,
            attempt_count,
        ) = await generate_tts_with_retry(
            text=cleaned_text,
            requested_voice=voice,
            rate=rate,
            volume=volume,
            pitch=pitch,
        )

        try:
            cache_path.write_bytes(
                audio_bytes
            )
        except OSError:
            pass

        print(
            "已生成面试官语音："
            f"{cleaned_text[:50]}，"
            f"声音={used_voice}，"
            f"尝试次数={attempt_count}"
        )

        return Response(
            content=audio_bytes,
            media_type="audio/mpeg",
            headers={
                "Cache-Control": (
                    "public, max-age=86400"
                ),
                "X-TTS-Cache": "MISS",
                "X-TTS-Voice": used_voice,
                "X-TTS-Attempts": str(
                    attempt_count
                ),
            },
        )

    except HTTPException:
        raise

    except Exception as error:
        print(
            "生成面试官语音最终失败：",
            repr(error),
        )

        raise HTTPException(
            status_code=503,
            detail=(
                "面试官语音服务暂时不可用，"
                "本轮已切换为文字显示："
                f"{str(error)}"
            ),
            headers={
                "Retry-After": str(
                    max(
                        1,
                        int(
                            TTS_FAILURE_COOLDOWN_SECONDS
                        ),
                    )
                )
            },
        ) from error


@app.post(
    "/speech-to-text",
    response_model=SpeechToTextResponse,
)
def speech_to_text(
    file: UploadFile = File(...),
    input_type: str = Form("interview_answer"),
) -> SpeechToTextResponse:
    filename = file.filename or "recording.wav"

    allowed_suffixes = {
        ".wav",
        ".mp3",
        ".m4a",
        ".ogg",
        ".flac",
        ".webm",
        ".mp4",
    }

    suffix = Path(filename).suffix.lower()

    if suffix not in allowed_suffixes:
        raise HTTPException(
            status_code=400,
            detail=(
                "不支持该音频格式。"
                "请上传WAV、MP3、M4A、OGG、FLAC、"
                "WEBM或MP4音频。"
            ),
        )

    audio_bytes = file.file.read(
        MAX_AUDIO_FILE_BYTES + 1
    )

    if not audio_bytes:
        raise HTTPException(
            status_code=400,
            detail="上传的音频文件为空。",
        )

    if len(audio_bytes) > MAX_AUDIO_FILE_BYTES:
        raise HTTPException(
            status_code=413,
            detail="音频文件过大，请缩短录音后重试。",
        )

    temporary_path = ""

    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            suffix=suffix,
            delete=False,
        ) as temporary_file:
            temporary_file.write(audio_bytes)
            temporary_path = temporary_file.name

        normalized_input_type = (
            input_type.strip().lower()
        )

        if normalized_input_type not in {
            "target_position",
            "interview_answer",
        }:
            normalized_input_type = "interview_answer"

        print(
            f"收到语音文件：{filename}，"
            f"大小：{len(audio_bytes)}字节，"
            f"识别用途：{normalized_input_type}"
        )

        result = transcribe_audio_file(
            temporary_path,
            normalized_input_type,
        )

        print("语音识别结果：", result.text)

        return result

    except HTTPException:
        raise

    except Exception as error:
        print(
            "语音识别失败：",
            repr(error),
        )

        raise HTTPException(
            status_code=500,
            detail=(
                "语音识别失败："
                f"{str(error)}"
            ),
        ) from error

    finally:
        try:
            file.file.close()
        except Exception:
            pass

        if (
            temporary_path
            and os.path.exists(temporary_path)
        ):
            try:
                os.remove(temporary_path)
            except OSError:
                pass


@app.post(
    "/next-question",
    response_model=NextQuestionResponse,
)
def generate_next_question(
    data: NextQuestionRequest,
) -> NextQuestionResponse:
    if (
        len(data.previous_questions)
        != len(data.previous_answers)
    ):
        raise HTTPException(
            status_code=400,
            detail="历史问题和回答数量必须一致。",
        )

    expected_history_count = (
        data.question_number - 1
    )

    if (
        len(data.previous_questions)
        != expected_history_count
    ):
        raise HTTPException(
            status_code=400,
            detail=(
                f"生成第{data.question_number}轮时，"
                f"应提交{expected_history_count}组历史记录。"
            ),
        )

    history_text = build_history_text(
        data.previous_questions,
        data.previous_answers,
    )

    last_answer = (
        data.previous_answers[-1].strip()
        if data.previous_answers
        else ""
    )

    answer_quality_hint = (
        "上一轮回答很简短。请优先追问具体事例、"
        "行动过程或结果，但不要机械复述固定句子。"
        if last_answer
        and is_answer_too_short(last_answer)
        else (
            "请结合上一轮回答中的具体内容作出简短回应，"
            "再提出最自然的下一问。"
            if last_answer
            else "这是第一轮，不需要评价候选人的回答。"
        )
    )

    system_prompt = """
你是一名专业、友好、严谨的中文AI面试官，
面试对象是大学生或应届毕业生。

你的任务不是只生成一个孤立问题，
而是生成面试官下一轮完整的自然回应。

必须返回四个字段：

1. reaction
对候选人上一轮回答作出的简短、中性、自然回应。
第一轮可用于欢迎候选人并自然引入面试。
不要给分，不要说“回答得很好”“表现一般”“还可以”等评价。
不要编造候选人没有说过的内容。
长度建议为0到35个汉字。

2. question
接下来真正要问的问题。
必须简洁清晰，原则上不超过45个汉字。
尽量根据上一轮回答追问，不要重复已有问题。

3. is_follow_up
如果问题是在继续深挖上一轮回答，返回true。
如果是转向新的面试主题，返回false。

4. speech_text
面试官实际要朗读的一整段话。
应当自然地把reaction和question连接在一起。
不要机械添加“下一题是”。
如果是追问，更不能说“下一个问题”或“下一题”。
不要添加“第几题”等编号。

面试结构建议：

第1轮：
欢迎语、自我介绍、求职动机或岗位理解。

第2轮：
项目经历与个人贡献，或根据第一轮内容自然追问。

第3轮：
技术细节、困难、解决过程或结果。

第4轮：
团队合作、沟通协调、情景处理或发展计划。

只输出合法JSON，不要输出解释、Markdown或代码块。

格式必须为：

{
  "reaction": "自然回应",
  "question": "问题",
  "is_follow_up": true,
  "speech_text": "自然回应与问题组成的完整口语"
}
""".strip()

    user_prompt = f"""
目标岗位：
{data.target_position}

当前是第{data.question_number}轮。

历史面试记录：
{history_text}

本轮提示：
{answer_quality_hint}

请根据真实上下文生成一轮自然的面试官回应。
只返回规定的JSON。
""".strip()

    try:
        deepseek_client = ensure_client()

        response = (
            deepseek_client
            .chat.completions.create(
                model=DEEPSEEK_MODEL,
                messages=[
                    {
                        "role": "system",
                        "content": system_prompt,
                    },
                    {
                        "role": "user",
                        "content": user_prompt,
                    },
                ],
                response_format={
                    "type": "json_object"
                },
                temperature=0.45,
                max_tokens=350,
                stream=False,
            )
        )

        content = (
            response.choices[0]
            .message.content
        )

        if not content:
            raise ValueError(
                "DeepSeek返回了空的面试轮次。"
            )

        model_result = parse_json_content(
            content
        )

        reaction = str(
            model_result.get(
                "reaction",
                "",
            )
        ).strip()

        question = str(
            model_result.get(
                "question",
                "",
            )
        ).strip()

        is_follow_up = bool(
            model_result.get(
                "is_follow_up",
                False,
            )
        )

        speech_text = str(
            model_result.get(
                "speech_text",
                "",
            )
        ).strip()

        if not question:
            raise ValueError(
                "DeepSeek没有返回question字段。"
            )

        if not speech_text:
            speech_text = build_speech_text(
                reaction,
                question,
            )

        return NextQuestionResponse(
            reaction=reaction,
            question=question,
            speech_text=speech_text,
            is_follow_up=is_follow_up,
        )

    except Exception as error:
        print(
            "DeepSeek生成自然面试轮次失败：",
            repr(error),
        )

        fallback_turn = get_fallback_turn(
            data.question_number,
            data.target_position,
            data.previous_answers,
        )

        print(
            "已使用备用面试轮次：",
            fallback_turn.speech_text,
        )

        return fallback_turn


@app.post(
    "/score",
    response_model=InterviewScoreResponse,
)
def score_interview(
    data: InterviewRequest,
) -> InterviewScoreResponse:
    target_position = (
        data.target_position.strip()
        or "未指定岗位"
    )

    cleaned_answers = [
        answer.strip()
        for answer in data.answers
    ]

    if len(cleaned_answers) != 4:
        raise HTTPException(
            status_code=400,
            detail="必须提交四道问题的回答。",
        )

    if any(
        not answer
        for answer in cleaned_answers
    ):
        raise HTTPException(
            status_code=400,
            detail="四道问题都必须填写回答。",
        )

    if len(data.questions) == 4:
        cleaned_questions = [
            question.strip()
            for question in data.questions
        ]
    else:
        cleaned_questions = (
            DEFAULT_INTERVIEW_QUESTIONS
        )

    interview_text = build_interview_text(
        cleaned_questions,
        cleaned_answers,
    )

    system_prompt = """
你是一名严谨、公平、善于提供可执行建议的
应届毕业生面试评估员。

请结合目标岗位、四道真实问题和候选人的四段回答，
给出具体、个性化、可追溯的面试评价。

评分维度：

1. language_score，语言表达，0到100分。
考察回答是否清晰、连贯、有逻辑、有重点，
是否能够使用结构化表达说明经历。

2. reaction_score，临场反应，0到100分。
考察是否正面回答问题、能否理解追问、
分析问题并作出合理回应。

3. professional_score，专业匹配度，0到100分。
考察岗位相关技能、项目经历、个人贡献、
技术或业务理解、问题解决过程和成果。

评分原则：

- 不得只根据回答长短评分。
- 回答过短、空泛、重复或答非所问时应降低分数。
- 有具体情境、个人行动、方法、工具和结果时可提高分数。
- 面向大学生和应届毕业生，不按资深从业者标准评分。
- 不评价外貌、性别、学校层次等无关因素。
- 分数必须与文字评价相互一致。
- 不得编造候选人没有提到的项目、工具、数字或成果。

表现亮点 highlights：

- 必须返回3到4条。
- 每条应为完整中文句子，建议35到80个汉字。
- 至少2条必须引用或概括候选人回答中的具体内容，
  例如实际项目、个人职责、使用工具、解决方法或结果。
- 除了说明“做了什么”，还要说明这体现了什么能力。
- 不要写“完成了全部问题”“表现较好”这类空泛套话。
- 不要添加“表现亮点”标题。

改进建议 suggestions：

- 必须返回3到4条。
- 每条应为完整中文句子，建议40到100个汉字。
- 每条都要指出一个具体不足，并给出可执行的修改方法。
- 尽量结合对应问题或回答内容，
  说明下一次可以补充哪些背景、行动、技术细节或量化结果。
- 至少1条要提供可直接采用的回答结构，
  例如STAR结构或“观点—依据—案例—结果”。
- 不要写“继续努力”“加强能力”这类无法执行的套话。
- 不要添加“改进建议”标题。

只输出合法JSON，不要输出解释、Markdown或代码块。

格式必须为：

{
  "language_score": 80,
  "reaction_score": 75,
  "professional_score": 78,
  "highlights": [
    "第一条具体亮点",
    "第二条具体亮点",
    "第三条具体亮点"
  ],
  "suggestions": [
    "第一条具体建议",
    "第二条具体建议",
    "第三条具体建议"
  ]
}
""".strip()

    user_prompt = f"""
目标岗位：
{target_position}

完整模拟面试记录：
{interview_text}

请严格依据上述真实内容进行评分。
亮点和建议必须具体引用候选人的回答，
不得为了写得丰富而编造事实。

请只返回规定的JSON。
""".strip()

    try:
        deepseek_client = ensure_client()

        response = (
            deepseek_client
            .chat.completions.create(
                model=DEEPSEEK_MODEL,
                messages=[
                    {
                        "role": "system",
                        "content": system_prompt,
                    },
                    {
                        "role": "user",
                        "content": user_prompt,
                    },
                ],
                response_format={
                    "type": "json_object"
                },
                temperature=0.25,
                max_tokens=1400,
                stream=False,
            )
        )

        content = (
            response.choices[0]
            .message.content
        )

        if not content:
            raise ValueError(
                "DeepSeek返回了空评分。"
            )

        model_result = parse_json_content(
            content
        )

        language_score = clamp_score(
            model_result.get(
                "language_score"
            )
        )

        reaction_score = clamp_score(
            model_result.get(
                "reaction_score"
            )
        )

        professional_score = clamp_score(
            model_result.get(
                "professional_score"
            )
        )

        demeanor_score = 80

        overall_score = round(
            language_score * 0.30
            + reaction_score * 0.20
            + professional_score * 0.40
            + demeanor_score * 0.10
        )

        (
            fallback_highlights,
            fallback_suggestions,
        ) = build_fallback_feedback(
            target_position,
            cleaned_questions,
            cleaned_answers,
        )

        highlights = format_feedback_section(
            model_result.get(
                "highlights"
            ),
            fallback_highlights.splitlines(),
        )

        suggestions = format_feedback_section(
            model_result.get(
                "suggestions"
            ),
            fallback_suggestions.splitlines(),
        )

        return InterviewScoreResponse(
            overall_score=overall_score,
            language_score=language_score,
            reaction_score=reaction_score,
            professional_score=professional_score,
            demeanor_score=demeanor_score,
            highlights=highlights,
            suggestions=suggestions,
        )

    except Exception as error:
        print(
            "DeepSeek评分失败：",
            repr(error),
        )

        print("已使用详细备用评分。")

        return calculate_fallback_score(
            target_position,
            cleaned_questions,
            cleaned_answers,
        )
