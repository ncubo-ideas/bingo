FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jdk-headless rsync unzip \
 && rm -rf /var/lib/apt/lists/*

ENV JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64
ENV ANDROID_HOME=/opt/android-sdk
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV USER=root

RUN dotnet workload install maui-android

COPY Docker/nuget.docker.config /seed/NuGet.config
COPY Directory.Build.props /seed/
COPY App/BingoApp/BingoApp.csproj App/BingoApp/nuget.config /seed/App/BingoApp/
COPY App/BingoApp/localfeed/ /seed/App/BingoApp/localfeed/
COPY Engine/BingoEngine/BingoEngine.csproj /seed/Engine/BingoEngine/
COPY Engine/Village/Village.csproj /seed/Engine/Village/

RUN cd /seed \
 && dotnet build App/BingoApp/BingoApp.csproj -t:InstallAndroidDependencies -f net10.0-android \
      -p:AndroidSdkDirectory=$ANDROID_HOME -p:JavaSdkDirectory=$JAVA_HOME -p:AcceptAndroidSDKLicenses=True \
 && dotnet restore App/BingoApp/BingoApp.csproj -p:RuntimeIdentifier=android-x64

ENTRYPOINT ["sh", "-c", "tr -d '\\r' < /repo/Docker/build.sh | sh"]
