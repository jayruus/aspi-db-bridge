#!/bin/bash

# Script per creare un archivio ZIP con solo i file necessari per il deploy su hosting Aruba
# Include .env.production come .env per produzione

PROJECT_DIR="/Users/jacopoturchi/Documents/private/projects/aspi/aspi-asp/DbBridge"


# Rimuovi eventuali ZIP precedenti
rm -rf "$PROJECT_DIR/publish-win-x64"

cd "$PROJECT_DIR"

dotnet publish -c Release -r win-x64 -o ./publish-win-x64

cp ../app_offline.htm ./publish-win-x64/app_offline.htm

