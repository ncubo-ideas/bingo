#!/bin/sh
set -e

mkdir -p /work/App /work/Engine
rsync -a /repo/Directory.Build.props /work/
rsync -a --delete --exclude bin --exclude obj --exclude .vs /repo/App/BingoApp /work/App/
rsync -a --delete --exclude bin --exclude obj --exclude .vs /repo/Engine/BingoEngine /repo/Engine/Village /work/Engine/
cp /seed/NuGet.config /work/NuGet.config

# The signing key is not in the repo. With Docker/debug.keystore every build is signed the
# same, installs over the previous one and matches the site's assetlinks.json; without it
# the APK is signed with a throwaway debug key.
SIGNING=""
if [ -f /repo/Docker/debug.keystore ]; then
  SIGNING="-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=/repo/Docker/debug.keystore -p:AndroidSigningKeyAlias=androiddebugkey -p:AndroidSigningStorePass=android -p:AndroidSigningKeyPass=android"
else
  echo "Docker/debug.keystore not found: signing with a throwaway debug key"
fi

cd /work
rm -f App/BingoApp/bin/Debug/net10.0-android/android-x64/*-Signed.apk
dotnet build App/BingoApp/BingoApp.csproj -c Debug -f net10.0-android \
  -p:RuntimeIdentifier=android-x64 \
  -p:BingoRelay="${BINGO_RELAY:-1}" \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:AndroidPackageFormat=apk \
  $SIGNING

cp App/BingoApp/bin/Debug/net10.0-android/android-x64/com.ncubo.bingo-Signed.apk /out/bingo.apk
ls -la /out/bingo.apk
