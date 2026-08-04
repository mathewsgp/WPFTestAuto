# WpfSpyAgent.Grpc (optional alternate transport)

The framework's default, fully-implemented IPC transport is **Named
Pipes** (`WpfSpyAgent/SpyAgentHost.cs` + the Python `WPFSpyRealDriver` in
`drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py`). This folder is a
**not-wired-in, optional alternative** for teams that prefer gRPC —
e.g. because test execution happens on a different machine than the
application under test, or because the organization has standardized
tooling around gRPC.

`Protos/spy_agent.proto` defines the same six commands
(Find/Invoke/SetValue/GetText/IsVisible/Toggle) as the Named Pipe JSON
protocol (see `../docs/PROTOCOL.md`) — just as protobuf messages instead
of JSON lines.

## To wire this in for real

1. **Server side** (replaces `SpyAgentHost`'s Named Pipe listener):
   add `Grpc.AspNetCore` to `WpfSpyAgent.csproj`, generate the C# service
   base class from `spy_agent.proto`, and implement each RPC by calling
   the exact same `VisualTreeInspector`/`CommandDispatcher` methods
   already used by the Named Pipe path — dispatched onto the WPF UI
   thread the same way (`Dispatcher.Invoke`).
2. **Client side** (replaces `WPFSpyRealDriver._send`): add `grpcio` +
   `grpcio-tools` to `requirements.txt`, generate the Python client stub
   from the same `.proto` file (`python -m grpc_tools.protoc ...`), and
   replace the named-pipe socket calls with generated stub calls
   (`stub.Invoke(ElementRequest(name=...))`).
3. Everything above Layer 4 (Layer 3's `_resolve_and_execute`, all of
   Layer 1/2, the repositories) needs **zero changes** — this is exactly
   the API-parity contract the whole framework is built around.

This is intentionally left as a documented option rather than a second
fully-maintained implementation, to avoid two parallel IPC stacks in a
reference project. Named Pipes is sufficient for same-host test execution
against a WPF app, which is the overwhelmingly common case.
