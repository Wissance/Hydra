FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-test

WORKDIR /test
RUN mkdir -p -v /test_result
COPY Wissance.Hydra/Wissance.Hydra .
SHELL [ "bash", "-c" ]
ENV IS_CONTAINERIZED=true
EXPOSE 10000-15000
RUN dotnet build Wissance.Hydra.sln

ENTRYPOINT ["dotnet", "test", "--logger:console;verbosity=normal"]