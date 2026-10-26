using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RippersCasino.Client;
using RippersCasino.Core;

internal static class ClientLifecycleTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Func<bool> condition) => test("client lifecycle / " + name, () =>
        {
            if (!condition()) throw new Exception(name);
        });
        Check("closed casino cannot poll in hideout, menu or either raid context", () =>
        {
            var session = new CasinoReadSession();
            return !session.CanPoll(true) && !session.CanPoll(false) && !session.Accept(session.Generation);
        });
        Check("open casino can poll only in hideout", () =>
        {
            var session = new CasinoReadSession();
            return session.Begin() && session.CanPoll(true) && !session.CanPoll(false);
        });
        Check("closing invalidates a pending read immediately", () =>
        {
            var session = new CasinoReadSession(); session.Begin();
            var ticket = session.Generation;
            session.End();
            return !session.Accept(ticket) && !session.CanPoll(true);
        });
        Check("late completion cannot update a reopened display", () =>
        {
            var session = new CasinoReadSession(); session.Begin();
            var old = session.Generation;
            session.End(); session.Begin();
            return !session.Accept(old) && session.Accept(session.Generation);
        });
        Check("duplicate open and close are idempotent across repeated use", () =>
        {
            var session = new CasinoReadSession();
            for (var i = 0; i < 10000; i++)
            {
                if (!session.Begin()) return false;
                var generation = session.Generation;
                if (session.Begin() || generation != session.Generation) return false;
                session.End(); var closed = session.Generation; session.End();
                if (session.Generation != closed || session.Accept(generation)) return false;
            }
            return !session.Active;
        });
        Check("closed read session permits the original wager to settle and replay exactly once", () =>
        {
            var session = new CasinoReadSession(); session.Begin();
            var wallet = new Wallet { Chips = 10 };
            var engine = new CasinoEngine(new CasinoConfig(), new Tickets([0, 1, 3]));
            var command = new CasinoCommand(Guid.NewGuid().ToString("N"), 1, "spin", "RUB", 1, "rippers-slot");
            var inventory = new DigitalChipInventory(wallet, new Money(0), CashierTerms.MaximumChips);
            session.End();
            var receipt = engine.Apply(wallet, command, inventory);
            engine.Apply(wallet, command, inventory);
            return !session.Active && wallet.Chips == 9 && wallet.NextSequence == 2 && wallet.LastRequestId == receipt.Id;
        });
        Check("new session accepts its own fresh snapshot", () =>
        {
            var session = new CasinoReadSession(); session.Begin(); session.End(); session.Begin();
            return session.Accept(session.Generation) && session.CanPoll(true);
        });
        Check("closing a display clears delayed revolver audio without replaying the shot", () =>
        {
            var trigger = new RevolverTrigger(); trigger.Seed("round", 0);
            trigger.Schedule("round", 1, "eliminated", 10);
            trigger.Seed("", 0);
            return !trigger.Pending && !trigger.TryFire(100, out _);
        });
        test("client IL / raid tick contains no periodic searches, requests or file I/O", () =>
            NoCalls("CasinoPlugin", "Tick", "Object::Find", "Resources::Find", "RequestHandler::", "File::", "JsonConvert::", "Texture2D::.ctor"));
        test("client IL / closed laptop interaction contains no scans, timers, network, audio or artwork loading", () =>
            NoCalls("CasinoPlugin", "TickModule", "Object::Find", "Resources::Find", "RequestHandler::", "File::", "CasinoAudio::", "LoadCasino", "Time::get_unscaledTime"));
        test("client IL / plugin startup defers graphics and audio resources", () =>
            NoCalls("CasinoPlugin", "Initialize", "LoadCasinoAssets", "LoadModuleIcon", "CasinoAudio::.ctor", "Texture2D::.ctor"));
        test("client IL / idle plugin has no Unity OnGUI callback", () =>
        {
            Require(!HasMethod("CasinoPlugin", "OnGUI") && HasMethod("CasinoGui", "OnGUI"), "IMGUI must belong to a separately suspended component");
            Require(Calls("CasinoPlugin", "FinishClose").Any(c => c.EndsWith("CasinoPlugin::SetGuiVisible")), "close must suspend the drawing component");
        });
        test("client IL / Intel stage registration repairs live data instead of short-circuiting on stage history", () =>
        {
            var calls = Calls("CasinoPlugin", "RegisterLaptopStage");
            Require(calls.Any(c => c.EndsWith("StageInteractionRegistration::Ensure")), "stage binding must check live interaction data");
            Require(!calls.Any(c => c.EndsWith("::Contains")), "stage history cannot prove an interaction is still present");
            NoCalls("StageInteractionRegistration", "Ensure", "Object::Find", "Resources::Find", "RequestHandler::", "File::");
        });
        test("client IL / existing Intel panel hook rebinds the current stage", () =>
        {
            Require(Calls("IntelligenceCenterAccess", "LaptopStage").Any(c => c.EndsWith("CasinoPlugin::RegisterLaptopStage")), "current-stage binding hook missing");
        });
        test("client IL / close never stops financial coroutines", () =>
        {
            var calls = Calls("CasinoPlugin", "Close");
            Require(calls.Any(c => c.EndsWith("CasinoReadSession::End")) && calls.Any(c => c.EndsWith("CasinoAudio::Suspend")), "close must invalidate reads and suspend audio");
            Require(!calls.Any(c => c.Contains("StopAllCoroutines") || c.Contains("StopCoroutine") || c.EndsWith("File::Delete")), "close must preserve committed work and recovery files");
        });
        test("client IL / table reads are gated and stale responses rejected before deserialization", () => ReadGuards("<PollTableSession>"));
        test("client IL / wallet reads are gated and stale responses rejected before application", () => ReadGuards("<Refresh>"));
        test("client IL / financial coroutine retains native completion, receipt reconciliation and durable cleanup", () =>
        {
            var calls = Calls("<Submit>", "MoveNext");
            Require(calls.Any(c => c.EndsWith("CasinoPlugin::ReadState")) && calls.Any(c => c.EndsWith("CasinoPlugin::ClearPending")), "financial completion path removed");
            Require(!calls.Any(c => c.Contains("CasinoReadSession::")), "financial operation must outlive read session cancellation");
        });
        test("client IL / scene subscription is installed and removed exactly once", () =>
        {
            Require(Calls("CasinoPlugin", "Initialize").Count(c => c.EndsWith("SceneManager::add_sceneUnloaded")) == 1, "duplicate scene subscription");
            Require(Calls("CasinoPlugin", "DisposePlugin").Count(c => c.EndsWith("SceneManager::remove_sceneUnloaded")) == 1, "scene subscription leak");
        });
        test("client IL / audio configuration subscription has one matching removal", () =>
        {
            Require(Calls("CasinoAudio", ".ctor").Count(c => c.EndsWith("AudioSettings::add_OnAudioConfigurationChanged")) == 1, "duplicate audio subscription");
            Require(Calls("CasinoAudio", "Dispose").Count(c => c.EndsWith("AudioSettings::remove_OnAudioConfigurationChanged")) == 1, "audio subscription leak");
        });
        test("client IL / read result cannot synchronously wait on an incomplete task", () =>
        {
            var calls = Calls("CasinoPlugin", "ReadState");
            Require(calls.FindIndex(c => c.EndsWith("Task::get_IsCompleted")) < calls.FindIndex(c => c.EndsWith("GetAwaiter")), "completion check must precede result access");
            Require(calls.Any(c => c.EndsWith("Task::get_IsCompleted")), "missing completion check");
        });
    }
    private static void ReadGuards(string type)
    {
        var calls = Calls(type, "MoveNext");
        var request = calls.FindIndex(c => c.Contains("RequestHandler::"));
        Require(request >= 0 && calls.FindIndex(c => c.EndsWith("CasinoReadSession::CanPoll")) is var guard && guard >= 0 && guard < request, "read must be gated before starting network work");
        var apply = calls.FindIndex(c => c.Contains("DeserializeObject") || c.EndsWith("CasinoPlugin::ReadState"));
        Require(apply > request && calls.Skip(request + 1).Take(apply - request - 1).Any(c => c.EndsWith("CasinoReadSession::Accept")), "read must reject obsolete display generation before applying result");
    }
    private static void NoCalls(string type, string method, params string[] forbidden)
    {
        var found = Calls(type, method).Where(call => forbidden.Any(call.Contains)).ToArray();
        Require(found.Length == 0, "idle path contains " + string.Join(", ", found));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static bool HasMethod(string type, string method)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../client/bin/Release/net472/RippersCasino.Client.dll"));
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Where(t => metadata.GetString(t.Name) == type)
            .Any(t => t.GetMethods().Select(metadata.GetMethodDefinition).Any(m => metadata.GetString(m.Name) == method));
    }
    private static List<string> Calls(string type, string method)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../client/bin/Release/net472/RippersCasino.Client.dll"));
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(t => type.StartsWith('<') ? metadata.GetString(t.Name).StartsWith(type, StringComparison.Ordinal) : metadata.GetString(t.Name) == type);
        var target = definition.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == method);
        var bytes = pe.GetMethodBody(target.RelativeVirtualAddress).GetILBytes()!;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var calls = new List<string>();
        for (var offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var opcode = opcodes[value];
            if (opcode.OperandType == OperandType.InlineMethod)
                calls.Add(Name(MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset))));
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
        return calls;
        string Name(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Name(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var m = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return metadata.GetString(metadata.GetTypeDefinition(m.GetDeclaringType()).Name) + "::" + metadata.GetString(m.Name);
            }
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
            var owner = member.Parent.Kind == HandleKind.TypeReference ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name) : "Generic";
            return owner + "::" + metadata.GetString(member.Name);
        }
    }
}
