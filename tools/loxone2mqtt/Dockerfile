# Pinned to 3.12 (not 3.13+): pyloxone-api 0.2.4 depends on httpx<0.20, which imports the
# stdlib `cgi` module. `cgi` was removed in Python 3.13, so this sidecar cannot run on a
# newer base image without breaking pyloxone-api's import chain.
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
CMD ["python", "loxone2mqtt.py"]
