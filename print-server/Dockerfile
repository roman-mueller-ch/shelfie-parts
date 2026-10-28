# Shelfie print server: FastAPI + ptouch-print for a Brother P-touch over USB.

# ---------------------------------------------------------------- ptouch-print
# Not packaged in Debian 13. Built from source, pinned to the v1.9 commit.
# The upstream server (git.familie-radermacher.ch) serves an incomplete
# repository over dumb HTTP, so we clone a mirror; the commit hash matches
# upstream's v1.9 tag, which guarantees identical sources.
FROM debian:trixie-slim AS ptouch
ARG PTOUCH_REPO=https://git.pfzetto.de/mirror/ptouch-print.git
ARG PTOUCH_COMMIT=02be73c75be483bd2bf947a048d447200e3229af
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates git cmake make gcc libc6-dev pkg-config gettext \
      libgd-dev libusb-1.0-0-dev \
 && rm -rf /var/lib/apt/lists/*
RUN git clone "$PTOUCH_REPO" /src \
 && git -C /src checkout --detach "$PTOUCH_COMMIT" \
 && test "$(git -C /src rev-parse HEAD)" = "$PTOUCH_COMMIT" \
 && cmake -S /src -B /src/build -DCMAKE_BUILD_TYPE=Release \
 && make -C /src/build -j"$(nproc)"

# ---------------------------------------------------------------- app
FROM python:3.13-slim-trixie
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgd3 libusb-1.0-0 fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*
COPY --from=ptouch /src/build/ptouch-print /usr/local/bin/ptouch-print

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY app.py .

# Root is needed to open the USB device without a udev rule on the host.
# The container has no network of its own; it shares the Tailscale sidecar's.
EXPOSE 8000
CMD ["uvicorn", "app:app", "--host", "127.0.0.1", "--port", "8000"]
