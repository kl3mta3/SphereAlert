#!/bin/sh
set -e

# Ensure the data volume exists before the app touches the database / keyfile.
mkdir -p "${SPHEREALERT_DATA_DIR:-/data}"

exec dotnet SphereAlert.dll
