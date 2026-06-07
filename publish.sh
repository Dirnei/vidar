#!/usr/bin/env bash
set -euo pipefail

REGISTRY="${VIDAR_REGISTRY:-10.220.220.10:5000}"
TAG="${1:-latest}"

IMAGES=(
  "vidar-host:src/Vidar.Host/Dockerfile"
  "vidar-comm-shelly:src/Vidar.Communication.Shelly/Dockerfile"
  "vidar-comm-zigbee2mqtt:src/Vidar.Communication.Zigbee2Mqtt/Dockerfile"
)

echo "Building and publishing to ${REGISTRY} with tag ${TAG}"
echo ""

for entry in "${IMAGES[@]}"; do
  NAME="${entry%%:*}"
  DOCKERFILE="${entry#*:}"
  FULL="${REGISTRY}/${NAME}:${TAG}"

  echo "=> ${NAME}"
  docker build -t "${FULL}" -f "${DOCKERFILE}" .
  docker push "${FULL}"
  echo ""
done

echo "Done. Published:"
for entry in "${IMAGES[@]}"; do
  NAME="${entry%%:*}"
  echo "  ${REGISTRY}/${NAME}:${TAG}"
done
