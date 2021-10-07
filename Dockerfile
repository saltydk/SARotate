FROM saltydk/ubuntu-s6overlay:latest

ARG DEBIAN_FRONTEND="noninteractive"

# install packages
RUN pip3 install apprise

RUN curl -Ls -o "/app/SARotate" https://api.github.com/repos/saltydk/sarotate/releases/latest | grep "browser_download_url" | cut -d '"' -f 4 | wget -qi - && \
    chmod -R u=rwX,go=rX "/app" && \
    chmod +x /app/SARotate

COPY root/ /