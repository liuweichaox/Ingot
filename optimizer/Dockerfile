FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app

COPY optimizer/pyproject.toml optimizer/README.md ./
COPY optimizer/ingot_optimizer ./ingot_optimizer
RUN pip install --no-cache-dir ".[service]"

COPY optimizer/service.py ./service.py

USER 65534
EXPOSE 8100

CMD ["uvicorn", "service:app", "--host", "0.0.0.0", "--port", "8100"]
