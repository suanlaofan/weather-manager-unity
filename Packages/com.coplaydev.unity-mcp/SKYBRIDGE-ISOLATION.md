# Weather Manager local MCP configuration

Embedded MCP for Unity 10.1.0 and local project configuration use http://127.0.0.1:8097.
WeatherManagerLocalMcp.json controls this project only. The editor directly connects to an externally launched MCP HTTP server.
Project-local guards disable automatic host-client config rewrites, package migration config rewrites, automatic server launches, and setup prompts.
The project does not change the shared 8080 endpoint or other Unity instances. No credentials are stored in this file.

Server: mcp-for-unity --transport http --http-host 127.0.0.1 --http-port 8097 --project-scoped-tools
Client: official Python mcp.ClientSession / streamablehttp_client at http://127.0.0.1:8097/mcp.
Always validate mcpforunity://project/info against the current Weather Manager project path before invoking a tool.
