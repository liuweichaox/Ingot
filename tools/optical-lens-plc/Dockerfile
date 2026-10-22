FROM python:3.12-alpine

WORKDIR /app
COPY server.py /app/server.py

EXPOSE 1502 9100
ENTRYPOINT ["python", "/app/server.py"]
