FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-test

WORKDIR /test
RUN mkdir -p -v /test_result
COPY Wissance.Hydra/Wissance.Hydra .
SHELL [ "bash", "-c" ]
ENV IS_CONTAINERIZED=true
EXPOSE 10000-65535
# RUN dotnet restore
RUN dotnet build Wissance.Hydra.sln
#ENTRYPOINT error=0; \
#for dir in *.Tests; do \
#    # dotnet test --logger "junit;LogFilePath=/test_result/$dir.xml" $dir 2>&1 > /test_result/$dir.log; \
#    dotnet test --logger "console;verbosity=normal" \
#    exit_code=${PIPESTATUS[0]}; \
#    [ $exit_code -ne 0 ] && error=$exit_code; \
#done; \
#exit $error
ENTRYPOINT ["dotnet", "test", "--logger:console;verbosity=normal"]