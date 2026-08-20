#!/bin/bash
set -euo pipefail

echo "Installing OpenCode and agent-browser ..."
# Removed sudo to prevent file permission issues inside the Dev Container
npm install -g opencode-ai agent-browser ipro-cli @modelcontextprotocol/server-postgres


echo "Installing Chromium ..."
sudo apt-get update
sudo apt-get install -y chromium

echo "Adding the agent-browser skill to the OpenCode configuration ..."
npx -y skill add vercel-labs/agent-browser -a opencode -y

echo "Adding UIUX Pro Max to the OpenCode configuration ..."
# Configures the global design ruleset for the AI agent
npx -y skill add julianromli/opencode-template/skill/ui-ux-pro-max -a opencode -y

echo "Initializing UIUX Pro Max interactive layer..."
# Runs the initialization sequence for the UIUX framework
ipro init -i

echo "Adding SQL MCP to the OpenCode native toolset configuration ..."
# Registers the Postgres/SQL engine driver natively to the OpenCode MCP workspace routing layer
opencode mcp add postgres --command "npx" --args "-y, @modelcontextprotocol/server-postgres, postgresql://localhost:5432/mydb" --type local


echo "Verifying the installation ..."
opencode --version
# Check the installed npm package version safely
node -p "require('agent-browser/package.json').version"
ipro --version
opencode mcp list

echo "Setup completed successfully!"
