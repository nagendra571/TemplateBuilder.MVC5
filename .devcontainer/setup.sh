#!/bin/bash
set -euo pipefail

echo "Installing OpenCode and agent-browser ..."
# Removed sudo to prevent file permission issues inside the Dev Container
npm install -g opencode-ai agent-browser

echo "Installing Chromium ..."
sudo apt-get update
sudo apt-get install -y chromium

echo "Adding the agent-browser skill to the OpenCode configuration ..."
npx -y skill add vercel-labs/agent-browser -a opencode -y

echo "Verifying the installation ..."
opencode --version
# Check the installed npm package version safely
node -p "require('agent-browser/package.json').version"

echo "Setup completed successfully!"
