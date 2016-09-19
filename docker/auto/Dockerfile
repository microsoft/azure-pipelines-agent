FROM buildpack-deps:jessie-scm

RUN apt-get update && apt-get install -y --no-install-recommends \
    apt-transport-https \
    libunwind8 \
 && rm -rf /var/lib/apt/lists/*

RUN adduser --system --group vsts
WORKDIR /home/vsts

COPY ./start.sh .
RUN chown vsts:vsts start.sh

CMD ["./start.sh"]
