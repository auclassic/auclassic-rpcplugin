
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Reflection;
using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using System.IO;
using System.Reflection;

namespace CustomRpc
{
    [BepInPlugin("com.example.customrpc", "Custom RPC", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            RpcClient.Log = Log;

            Harmony harmony = new Harmony("com.amongus.classicrpc");

            try
            {
                MethodInfo updateMethod = AccessTools.Method(
                    typeof(Discord.ActivityManager),
                    nameof(Discord.ActivityManager.UpdateActivity));

                if (updateMethod != null)
                {
                    harmony.Patch(
                        updateMethod,
                        prefix: new HarmonyMethod(
                            typeof(UpdateActivityPatch).GetMethod(
                                nameof(UpdateActivityPatch.Prefix))));

                    Log.LogInfo("Patched Discord UpdateActivity");
                }
                else
                {
                    Log.LogError("UpdateActivity not found");
                }

                PatchManagerMethods(harmony);

                Log.LogInfo("Custom RPC loaded");
            }
            catch (Exception e)
            {
                Log.LogError("RPC patch loading failed: " + e);
            }
        }

        void PatchManagerMethods(Harmony harmony)
        {   
            string assemblyPath = Path.Combine(
            Paths.BepInExRootPath,
            "unhollowed",
            "Assembly-CSharp.dll"
        );

        if (File.Exists(assemblyPath))
        {
            try
            {
                Assembly.LoadFrom(assemblyPath);
                Log.LogInfo("Loaded generated Assembly-CSharp.dll");
            }
            catch (Exception e)
            {
                Log.LogError("Could not load Assembly-CSharp.dll: " + e);
            }
        }
            Type manager = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.Name == "IEFLFGBAIEB")
                        {
                            manager = type;
                            Log.LogInfo("Found manager: " + type.FullName);
                            Log.LogInfo("Manager assembly: " + assembly.GetName().Name);
                            break;
                        }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    foreach (Type type in e.Types)
                    {
                        if (type != null && type.Name == "IEFLFGBAIEB")
                        {
                            manager = type;
                            Log.LogInfo("Found manager: " + type.FullName);
                            Log.LogInfo("Manager assembly: " + assembly.GetName().Name);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.LogWarning("Could not inspect assembly " +
                        assembly.GetName().Name + ": " + e.Message);
                }

                if (manager != null)
                    break;
            }

            if (manager == null)
            {
                Log.LogError("IEFLFGBAIEB not found in loaded assemblies");

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    Log.LogInfo("Loaded assembly: " + assembly.GetName().Name);

                return;
            }

            string[] names =
            {
                "SetHowToPlay",
                "SetInLobbyClient",
                "SetInLobbyHost",
                "SetInMenus",
                "SetPlayingGame"
            };

            foreach (string name in names)
                PatchMethodsByName(harmony, manager, name);
        }

        void PatchMethodsByName(
            Harmony harmony,
            Type manager,
            string methodName)
        {
            MethodInfo[] methods = manager.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            int patched = 0;

            foreach (MethodInfo method in methods)
            {
                if (method.Name != methodName)
                    continue;

                try
                {
                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(
                            typeof(ManagerPatches).GetMethod(
                                nameof(ManagerPatches.Postfix),
                                BindingFlags.Public | BindingFlags.Static)));

                    Log.LogInfo("Patched " + methodName +
                        " (" + method.GetParameters().Length + " parameters)");

                    patched++;
                }
                catch (Exception e)
                {
                    Log.LogError("Failed to patch " + methodName + ": " + e);
                }
            }

            if (patched == 0)
                Log.LogWarning("Method not found: " + methodName);
        }
    }

    public static class UpdateActivityPatch
    {
        public static bool Prefix()
        {
            return false;
        }
    }

    
public static class ManagerPatches
{
    public static void Postfix(
        object[] __args,
        MethodBase __originalMethod)
    {
        try
        {
            string method = __originalMethod.Name;

            RpcClient.Log?.LogInfo("Game presence event: " + method);

            switch (method)
            {
                case "SetInMenus":
                    RpcClient.ResetTimer();
                    RpcClient.SendPresence(null, "In Menus");
                    break;

                case "SetHowToPlay":
                    RpcClient.SendPresence(null, "In Freeplay");
                    break;

                case "SetPlayingGame":
                    RpcClient.SendPresence(
                        "Playing",
                        "In Game",
                        timer: true);
                    break;

                case "SetInLobbyHost":
                    RpcClient.SendPresence(
                        "Hosting a game",
                        "In Lobby",
                        partyMax: 10,
                        largeText: "Ask to play!");
                    break;

                case "SetInLobbyClient":
                    RpcClient.SendPresence(
                        "Playing Among Us",
                        "In Lobby",
                        timer: true,
                        partyMax: 10);
                    break;
            }
        }
        catch (Exception e)
        {
            RpcClient.Log?.LogError("Manager postfix failed: " + e);
        }
    }
}

    public static class RpcClient
    {
        public const string ClientId = "1317572839754436730";
        public const string ButtonLabel = "Play now!";
        public const string ButtonUrl = "https://dsc.gg/amongusclassic";
        public const string DefaultDetails = "Playing Among Us v2020.11.17s";

        public static ManualLogSource Log;

        static readonly object Lock = new object();
        static readonly AutoResetEvent Signal = new AutoResetEvent(false);

        static NamedPipeClientStream pipe;
        static string pending;
        static Thread worker;
        static long? startTime;

        public static void ResetTimer()
        {
            startTime = null;
        }

        public static void SendPresence(
            string details,
            string state,
            bool timer = false,
            string partyId = null,
            int partySize = 0,
            int partyMax = 0,
            string largeText = null)
        {
            try
            {
                if (timer && startTime == null)
                    startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                string timestamps = timer && startTime.HasValue
                    ? Obj("start", Num(startTime.Value))
                    : null;

                string party = !string.IsNullOrEmpty(partyId) && partyMax > 0
                    ? Obj(
                        "id", Esc(partyId),
                        "size", "[" + partySize + "," + partyMax + "]")
                    : null;

                string assets = Obj(
                    "large_image", Esc("classic_new_years"),
                    "large_text", Str(largeText) ?? Esc("Among Us Classic"));

                string buttons = "[" + Obj(
                    "label", Esc(ButtonLabel),
                    "url", Esc(ButtonUrl)) + "]";

                string activityJson = Obj(
                    "details", Str(details) ?? Esc(DefaultDetails),
                    "state", Str(state),
                    "timestamps", timestamps,
                    "assets", assets,
                    "party", party,
                    "buttons", buttons
                );

                SetActivity(activityJson);
            }
            catch (Exception e)
            {
                Log?.LogWarning("SendPresence failed: " + e);
            }
        }

        public static void SetActivity(string activityJson)
        {
            string json =
                "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" +
                Process.GetCurrentProcess().Id +
                ",\"activity\":" + activityJson +
                "},\"nonce\":\"" + Guid.NewGuid() + "\"}";

            Log?.LogInfo("Presence: " + activityJson);

            lock (Lock)
            {
                pending = json;

                if (worker == null)
                {
                    worker = new Thread(Run)
                    {
                        IsBackground = true,
                        Name = "CustomRpc"
                    };

                    worker.Start();
                }
            }

            Signal.Set();
        }

        static void Run()
        {
            while (true)
            {
                Signal.WaitOne();

                string json;

                lock (Lock)
                {
                    json = pending;
                    pending = null;
                }

                if (json == null)
                    continue;

                try
                {
                    if (pipe == null || !pipe.IsConnected)
                        Connect();

                    Send(1, json);

                    string reply = ReadFrame();

                    Log?.LogInfo("Discord RPC response: " + reply);

                    if (reply.Contains("\"evt\":\"ERROR\""))
                        Log?.LogWarning("Discord rejected presence: " + reply);
                }
                catch (Exception e)
                {
                    Log?.LogWarning("Could not send presence: " + e.Message);
                    Close();
                }
            }
        }

        static void Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream p = new NamedPipeClientStream(
                    ".",
                    "discord-ipc-" + i,
                    PipeDirection.InOut,
                    PipeOptions.None);

                try
                {
                    p.Connect(250);
                }
                catch
                {
                    p.Dispose();
                    continue;
                }

                pipe = p;

                Send(0, "{\"v\":1,\"client_id\":\"" + ClientId + "\"}");

                string ready = ReadFrame();

                Log?.LogInfo(
                    "Connected to Discord (pipe " + i + "): " + ready);

                return;
            }

            throw new IOException("No Discord IPC pipe found");
        }

        static void Close()
        {
            try
            {
                pipe?.Dispose();
            }
            catch
            {
            }

            pipe = null;
        }

        static void Send(int opcode, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + body.Length];

            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);

            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();
        }

        static string ReadFrame()
        {
            byte[] header = ReadExact(8);
            int len = BitConverter.ToInt32(header, 4);

            if (len < 0 || len > 1024 * 1024)
                throw new IOException("Invalid IPC frame length");

            return Encoding.UTF8.GetString(ReadExact(len));
        }

        static byte[] ReadExact(int n)
        {
            byte[] buf = new byte[n];
            int offset = 0;

            while (offset < n)
            {
                int read = pipe.Read(buf, offset, n - offset);

                if (read <= 0)
                    throw new EndOfStreamException();

                offset += read;
            }

            return buf;
        }

        public static string Obj(params string[] kv)
        {
            StringBuilder sb = new StringBuilder("{");
            bool first = true;

            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                if (kv[i + 1] == null)
                    continue;

                if (!first)
                    sb.Append(',');

                first = false;

                sb.Append('"')
                    .Append(kv[i])
                    .Append("\":")
                    .Append(kv[i + 1]);
            }

            return sb.Append('}').ToString();
        }

        public static string Str(string s)
        {
            return s == null || s.Length < 2 ? null : Esc(s);
        }

        public static string Num(long value)
        {
            return value > 0 ? value.ToString() : null;
        }

        public static string Esc(string s)
        {
            StringBuilder sb = new StringBuilder("\"");

            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u")
                                .Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
