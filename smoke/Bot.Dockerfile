# UC-T14: the Linux player (Cursory.x86_64, Cursory_Data/, UnityPlayer.so) that plays every game role in
# smoke/docker-compose.yml, built into smoke/build/ by Unity.Cursory.EditorTools.BotBuild (tools/run-smoke.ps1 locally,
# .github/workflows/nightly.yml in CI). Same image as Unity.Multiplayer's smoke/Bot.Dockerfile; the role comes from
# the command line (-mode server, or -bot A|B).
#
#   docker build -f smoke/Bot.Dockerfile -t cursory-smoke:ci smoke
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --shell /bin/false game
WORKDIR /game
COPY build/ ./
RUN chmod +x Cursory.x86_64 && mkdir -p /shared && chown -R game:game /game /shared
USER game
# A plain Linux player still polls the mouse through SDL each frame; with no display (-batchmode -nographics in a
# container) SDL's default video driver crashes it (SIGSEGV in sdl::IsX11VideoDriver). The dummy driver doesn't.
ENV SDL_VIDEODRIVER=dummy
ENTRYPOINT ["./Cursory.x86_64"]
