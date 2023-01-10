FROM ubuntu:jammy

ARG DEBIAN_FRONTEND="noninteractive"

VOLUME ["/config"]

# install packages
RUN apt update && \
    apt install -y libicu-dev \
        ca-certificates jq curl \
        locales tzdata && \
# generate locale
    locale-gen en_US.UTF-8 && \
# clean up
    apt autoremove -y && \
    apt clean && \
    rm -rf /tmp/* /var/lib/apt/lists/* /var/tmp/*

# install packages
RUN pip3 install apprise

RUN mkdir "/app" && curl -L -o /app/SARotate $(curl -Ls https://api.github.com/repos/saltydk/sarotate/releases/latest | grep "browser_download_url" | cut -d '"' -f 4 | grep linux-x64) && \
    chmod -R u=rwX,go=rX "/app" && \
    chmod +x /app/SARotate

ENTRYPOINT ["/app/SARotate -v -c /config/config.yaml -l /config"]
