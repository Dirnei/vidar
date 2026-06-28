FROM python:3.12-slim

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY dyson2mqtt.py .

ENTRYPOINT ["python", "-u", "dyson2mqtt.py"]
