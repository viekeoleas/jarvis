import json
import struct
import sys

from piper.voice import PiperVoice


def write_frame(payload: bytes) -> None:
    sys.stdout.buffer.write(struct.pack("<i", len(payload)))
    sys.stdout.buffer.write(payload)
    sys.stdout.buffer.flush()


voice = PiperVoice.load(sys.argv[1])
sys.stdout.buffer.write(b"JRV1")
sys.stdout.buffer.flush()

for request_line in sys.stdin:
    try:
        text = json.loads(request_line)
        chunks = []
        for index, audio_chunk in enumerate(voice.synthesize(text)):
            if index > 0:
                chunks.append(bytes(int(audio_chunk.sample_rate * 0.2) * 2))
            chunks.append(audio_chunk.audio_int16_bytes)

        write_frame(b"".join(chunks))
    except Exception as error:
        message = str(error).encode("utf-8", errors="replace")
        sys.stdout.buffer.write(struct.pack("<i", -len(message)))
        sys.stdout.buffer.write(message)
        sys.stdout.buffer.flush()
