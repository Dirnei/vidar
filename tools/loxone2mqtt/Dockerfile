# Pinned to 3.9: pyloxone-api 0.2.4's pinned deps both require a pre-3.10 interpreter.
#   * httpx<0.20 imports the stdlib `cgi` module, removed in Python 3.13.
#   * websockets>=9.0.1,<10 passes `loop=` to asyncio.Lock/Event internally; that kwarg was
#     deprecated in 3.8 and REMOVED in 3.10, so on 3.10+ the WS handshake dies with
#     "Lock.__init__() got an unexpected keyword argument 'loop'" (seen at /auth/login -> 502).
# 3.9 is the newest base that satisfies both of pyloxone-api 0.2.4's dependency pins.
FROM python:3.9-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
CMD ["python", "loxone2mqtt.py"]
