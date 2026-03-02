FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-test

WORKDIR /test
COPY Wissance.Hydra/Wissance.Hydra .
SHELL [ "bash", "-c" ]
RUN dotnet build Wissance.Hydra.sln
ENTRYPOINT error=0; \
for dir in *.Tests; do \
    dotnet test --logger "junit;LogFilePath=/TestResults/$dir.xml" $dir 2>&1 > /TestResults/$dir.log; \
    exit_code=${PIPESTATUS[0]}; \
    [ $exit_code -ne 0 ] && error=$exit_code; \
done; \
exit $error