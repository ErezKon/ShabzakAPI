#!/bin/sh
# Container entrypoint: generates a JWT signing key if one was not provided
# via the Jwt__Key environment variable. This keeps secrets out of the image
# while ensuring the server can always start.

set -e

if [ -z "$Jwt__Key" ] || [ "$Jwt__Key" = "CHANGE_ME_TO_A_STRONG_256_BIT_SECRET_KEY_HERE!!" ]; then
    export Jwt__Key=$(head -c 32 /dev/urandom | base64)
    echo "========================================================"
    echo "  WARNING: No Jwt__Key provided — generated a random one."
    echo "  Tokens will be invalidated if the container restarts."
    echo "  Set Jwt__Key in your .env file for persistence."
    echo "========================================================"
fi

exec dotnet 04.ShabzakAPI.dll "$@"
