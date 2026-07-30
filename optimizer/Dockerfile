FROM ghcr.io/astral-sh/uv:0.11.32 AS uv
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_NO_DEV=1 \
    UV_CACHE_DIR=/tmp/uv-cache \
    PATH="/app/.venv/bin:$PATH"

WORKDIR /app

COPY --from=uv /uv /uvx /bin/
COPY optimizer/pyproject.toml optimizer/uv.lock optimizer/README.md ./
RUN uv sync --frozen --no-install-project --extra service

COPY optimizer/ingot_optimizer ./ingot_optimizer
COPY optimizer/service.py ./service.py
RUN uv sync --locked --no-editable --extra service && uv cache clean

USER 65534
EXPOSE 8100

CMD ["uv", "run", "--no-sync", "uvicorn", "service:app", "--host", "0.0.0.0", "--port", "8100"]
