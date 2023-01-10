FROM saltydk/ubuntu-s6overlay:latest

ARG DEBIAN_FRONTEND="noninteractive"

# install packages
RUN pip3 install apprise

RUN apt-get install libicu-dev

RUN curl -L -o /app/SARotate $(curl -Ls https://api.github.com/repos/saltydk/sarotate/releases/latest | grep "browser_download_url" | cut -d '"' -f 4 | grep linux-x64) && \
    chmod -R u=rwX,go=rX "/app" && \
    chmod +x /app/SARotate

COPY root/ /
