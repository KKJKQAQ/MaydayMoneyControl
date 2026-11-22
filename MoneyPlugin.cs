using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

[BepInPlugin("local.mayday.moneycontrol", "Mayday Money Control", "1.0.9")]
public sealed class MoneyPlugin : BasePlugin
{
    static string folder, session, lastId = "", result = "等待房主开局", state = "starting";
    static bool tileArmed;
    static Vector3 tilePosition;
    static long tileExpires;
    static UnityEngine.Object tileGame;
    static string TileCommand(string command, UnityEngine.Object game)
    {
        tileArmed = false;
        if (command == "tile:cancel") return "已取消地砖。";
        var owner = localPlayer?.GetValue(null) as UnityEngine.Component;
        if (owner == null || teleport == null) return "地砖接口不可用。";
        tilePosition = owner.transform.position;
        tileGame = game;
        tileExpires = Environment.TickCount64 + 120000;
        tileArmed = true;
        return "脚下已布置一次性地砖，120 秒后失效。";
    }
    static void TickTile(UnityEngine.Object game)
    {
        if (!tileArmed) return;
        if (game == null || game != tileGame || Environment.TickCount64 >= tileExpires)
        { tileArmed = false; result = "地砖已失效。"; return; }
        var owner = localPlayer?.GetValue(null) as UnityEngine.Object;
        if (owner == null) { tileArmed = false; return; }
        foreach (var member in crew.Values)
        {
            if (member == null || member == owner) continue;
            var pos = member.transform.position;
            float dx = pos.x - tilePosition.x, dz = pos.z - tilePosition.z;
            if (dx*dx + dz*dz > 0.64f || Math.Abs(pos.y - tilePosition.y) > 0.6f) continue;
            tileArmed = false;
            teleport.Invoke(member, new object[] { pos + new Vector3(0f,0.6f,0f), member.transform.eulerAngles.y });
            result = "地砖已触发，目标上移 0.6 米。";
            return;
        }
    }
    static long nextPoll;
    static PropertyInfo active, instance, money;
    static FieldInfo activeField;
    static MethodInfo addMoney;
    static PropertyInfo localRig, localFlag, localMotor, stamina, staminaMax, exhausted;
    static bool sprintEnabled, hostSession;
    static float multiplier = 1f;
    static UnityEngine.Object speedMotor;
    static PropertyInfo sprintSpeed, hopSprintSpeed;
    static float initialSpeed, initialHop;
    static string sprintStatus = "";
    static PropertyInfo crewList, crewId, crewName, inventory;
    static MethodInfo teleport, dropAll, stun;
    static PropertyInfo localPlayer, localInteractor, shoveImpulse;
    static float shoveMultiplier = 1f;
    static bool shoveHookReady;
    static readonly System.Collections.Generic.Dictionary<uint, UnityEngine.Component> crew = new();
    static BepInEx.Logging.ManualLogSource logger;

    public override void Load()
    {
        folder = Path.Combine(Paths.GameRootPath, "BepInEx", "config", "MaydayMoneyControl");
        Directory.CreateDirectory(folder);
        session = Guid.NewGuid().ToString("N");
        logger = Log;
        try
        {
            var application = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Application", true);
            var runInBackground = application.GetProperty("runInBackground", BindingFlags.Public | BindingFlags.Static);
            if (runInBackground != null) runInBackground.SetValue(null, true);
            var mirror = Assembly.Load("Mirror").GetType("Mirror.NetworkServer", true);
            var game = Assembly.Load("Assembly-CSharp").GetType("Mayday.GameState", true);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            active = mirror.GetProperty("active", flags);
            activeField = mirror.GetField("active", flags);
            instance = game.GetProperty("Instance", flags);
            money = game.GetProperty("NetworkMoney", flags);
            addMoney = game.GetMethod("ServerAddMoney", flags, null, new[] { typeof(int), typeof(string) }, null);
            var rig = game.Assembly.GetType("Mayday.Players.PlayerRig");
            var motor = game.Assembly.GetType("Mayday.Players.PlayerMotor");
            localRig = rig?.GetProperty("Local", flags);
            localFlag = rig?.GetProperty("IsLocal", flags);
            localMotor = rig?.GetProperty("Motor", flags);
            sprintSpeed = motor?.GetProperty("sprintSpeed", flags);
            hopSprintSpeed = motor?.GetProperty("hopSprintSpeed", flags);
            stamina = motor?.GetProperty("stamina", flags);
            staminaMax = motor?.GetProperty("staminaMax", flags);
            exhausted = motor?.GetProperty("exhausted", flags);

            if ((active == null && activeField == null) || instance == null || money == null || addMoney == null)
                throw new MissingMemberException("游戏接口不匹配；未修改任何资金。");
            var player = game.Assembly.GetType("Mayday.PlayerNet");
            crewList = player?.GetProperty("All", flags);
            crewId = player?.GetProperty("netId", flags);
            crewName = player?.GetProperty("DisplayName", flags);
            inventory = player?.GetProperty("Inventory", flags);
            teleport = player?.GetMethod("ServerTeleport", flags, null, new[] { typeof(Vector3), typeof(float) }, null);
            stun = player?.GetMethod("ServerStunSynced", flags, null, new[] { typeof(float) }, null);
            dropAll = inventory?.PropertyType.GetMethod("ServerDropAll", flags);
            localPlayer = player?.GetProperty("Local", flags);
            localInteractor = player?.GetProperty("Interactor", flags);
            var interactor = localInteractor?.PropertyType;
            shoveImpulse = interactor?.GetProperty("PlayerShoveImpulse", flags);
            var shove = interactor?.GetMethod("ServerShovePlayer", flags);
            if (shove != null && shoveImpulse?.CanWrite == true)
            {
                try
                {
                    new HarmonyLib.Harmony("local.mayday.hostshove").Patch(shove,
                        prefix: new HarmonyLib.HarmonyMethod(typeof(MoneyPlugin), nameof(BeforeShove)),
                        finalizer: new HarmonyLib.HarmonyMethod(typeof(MoneyPlugin), nameof(AfterShove)));
                    shoveHookReady = true;
                    Log.LogInfo("Host-only shove hook ready.");
                }
                catch (Exception e) { Log.LogWarning("Shove hook unavailable: " + e.GetBaseException().Message); }
            }
            AddComponent<MoneyUpdate>();
            state = "waiting";
            Publish(0);
            Log.LogInfo("Money control ready. Requests execute on game thread, host only.");
        }
        catch (Exception e)
        {
            state = "error"; result = e.GetBaseException().Message; Publish(0); Log.LogError(e);
        }
    }
    static void Publish(int balance)
    {
        var lines = new[] { session, DateTime.UtcNow.Ticks.ToString(), state, balance.ToString(), lastId, result.Replace('\r',' ').Replace('\n',' '), sprintStatus + (state == "ready" ? " | 推力：" + (shoveHookReady ? shoveMultiplier + "×" : "不可用") : "") };
        if (state == "ready")
        {
            var roster = new System.Collections.Generic.List<string>(lines);
            foreach (var member in crew)
            {
                string name = (crewName?.GetValue(member.Value) as string ?? "机组成员").Replace('\r',' ').Replace('\n',' ');
                roster.Add(member.Key + ":" + name);
            }
            lines = roster.ToArray();
        }
        string temp = Path.Combine(folder, "status.tmp"), dest = Path.Combine(folder, "status.txt");
        File.WriteAllLines(temp, lines);
        File.Move(temp, dest, true);
    }
    public static void Tick()
    {
        if (Environment.TickCount64 < nextPoll) return;
        nextPoll = Environment.TickCount64 + 250;
        try
        {
            bool host = Convert.ToBoolean(active != null ? active.GetValue(null) : activeField.GetValue(null));
            object gs = host ? instance.GetValue(null) : null;
            state = gs == null ? "waiting" : "ready";
            hostSession = gs != null;
            shoveMultiplier = 1f;
            string shoveFile = Path.Combine(folder, "shove-multiplier.txt");
            if (hostSession && shoveHookReady && File.Exists(shoveFile) &&
                float.TryParse(File.ReadAllText(shoveFile), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float shoveSetting) &&
                shoveSetting >= 1f && shoveSetting <= 20f) shoveMultiplier = shoveSetting;
            string speedFile = Path.Combine(folder, "sprint-multiplier.txt");
            multiplier = 1f;
            if (hostSession && File.Exists(speedFile) &&
                float.TryParse(File.ReadAllText(speedFile), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float configured) &&
                configured >= 1f && configured <= 10f) multiplier = configured;
            sprintEnabled = gs != null && File.Exists(Path.Combine(folder, "unlimited-sprint.on"));
            RefreshCrew(hostSession);
            TickTile(gs as UnityEngine.Object);
            int balance = gs == null ? 0 : Convert.ToInt32(money.GetValue(gs));
            string request = Path.Combine(folder, "request.txt");
            if (File.Exists(request))
            {
                // Consume before execution: a crash must never replay an addition.
                string claimed = Path.Combine(folder, "processing.txt");
                File.Move(request, claimed, true);
                string[] parts = File.ReadAllLines(claimed);
                File.Delete(claimed);
                string id = parts.Length > 1 ? parts[1] : "invalid";
                if (id != lastId)
                {
                    lastId = id;
                    int amount;
                    if (parts.Length != 3 || parts[0] != session) result = "请求已过期，请重新点击。";
                    else if (gs == null) result = "仅限房主：请先创建并进入自己的房间。";
                    else if (parts[2] == "tile:arm" || parts[2] == "tile:cancel") result = TileCommand(parts[2], gs as UnityEngine.Object);
                    else if (parts[2].StartsWith("drop:") || parts[2].StartsWith("sky:")) result = CrewAction(parts[2]);
                    else if (!parts[2].StartsWith("set:") || !int.TryParse(parts[2].Substring(4), out amount) || amount < 0) result = "请输入 0 到 2147483647 的目标余额，并使用新版面板。";
                    else
                    {
                        money.SetValue(gs, amount);
                        int after = Convert.ToInt32(money.GetValue(gs));
                        result = after == amount ? "余额已设定："  + balance + " → " + after : "游戏返回余额 " + after + "，请在游戏内核对。";
                        File.AppendAllText(Path.Combine(folder,"history.txt"), DateTime.UtcNow.ToString("O") + " | " + id + " | " + result + Environment.NewLine);
                        balance = after;
                    }
                }
            }
            Publish(balance);
        }
        catch (Exception e)
        {
            state = "error"; result = "操作失败：" + e.GetBaseException().Message;
            try { Publish(0); } catch { }
        }
    }
    public static void BeforeShove(object __instance, out float? __state)
    {
        __state = null;
        if (!hostSession || shoveMultiplier == 1f ||
            !Convert.ToBoolean(active != null ? active.GetValue(null) : activeField.GetValue(null))) return;
        var player = localPlayer?.GetValue(null) as UnityEngine.Object;
        var ownerInteractor = player != null ? localInteractor?.GetValue(player) as UnityEngine.Object : null;
        if (ownerInteractor == null || ownerInteractor != (__instance as UnityEngine.Object)) return;
        __state = (float)shoveImpulse.GetValue(null);
        shoveImpulse.SetValue(null, __state.Value * shoveMultiplier);
    }

    public static void AfterShove(float? __state)
    {
        if (__state.HasValue) shoveImpulse.SetValue(null, __state.Value);
    }

    static void RefreshCrew(bool host)
    {
        crew.Clear();
        if (!host || crewList == null || crewId == null) return;
        var list = crewList.GetValue(null);
        if (list == null) return;
        var type = list.GetType();
        var count = type.GetProperty("Count");
        var item = type.GetProperty("Item");
        if (count == null || item == null) return;
        for (int i = 0; i < (int)count.GetValue(list); i++)
        {
            var player = item.GetValue(list, new object[] { i }) as UnityEngine.Component;
            if (player == null) continue;
            uint id = (uint)crewId.GetValue(player);
            if (id != 0) crew[id] = player;
        }
    }

    static string CrewAction(string command)
    {
        var fields = command.Split(':');
        if (fields.Length < 2 || !uint.TryParse(fields[1], out uint id) ||
            !crew.TryGetValue(id, out var player) || player == null) return "目标已离开或无法识别，请重新选择。";
        if (command.StartsWith("drop:"))
        {
            var bag = inventory?.GetValue(player) as UnityEngine.Object;
            if (bag == null || dropAll == null) return "目标物品栏暂不可用。";
            dropAll.Invoke(bag, null);
            return "已执行全部掉落。";
        }
        float height = 15f;
        if (fields.Length > 3 || (fields.Length == 3 &&
            (!int.TryParse(fields[2], out int meters) || meters < 1 || meters > 1000)))
            return "高度必须是 1 到 1000 米的整数。";
        if (fields.Length == 3) height = int.Parse(fields[2]);
        if (teleport == null || stun == null) return "当前版本的传送或眩晕接口不可用。";
        var transform = player.transform;
        teleport.Invoke(player, new object[] { transform.position + new Vector3(0f, height, 0f), transform.eulerAngles.y });
        stun.Invoke(player, new object[] { 2f });
        return "已向上传送 " + height + " 米并眩晕 2 秒。";
    }

    public static void RefreshLocalSprint()
    {
        if (localRig == null || localFlag == null || localMotor == null) return;
        try
        {
            var rig = hostSession && Convert.ToBoolean(active != null ? active.GetValue(null) : activeField.GetValue(null))
                ? localRig.GetValue(null) as UnityEngine.Object : null;
            var motor = rig != null && Convert.ToBoolean(localFlag.GetValue(rig))
                ? localMotor.GetValue(rig) as UnityEngine.Object : null;
            if (speedMotor != null && (speedMotor != motor || multiplier == 1f))
            {
                sprintSpeed.SetValue(speedMotor, initialSpeed);
                hopSprintSpeed.SetValue(speedMotor, initialHop);
                speedMotor = null;
            }
            sprintStatus = "";
            if (motor == null) return;
            if (multiplier != 1f && sprintSpeed?.CanWrite == true && hopSprintSpeed?.CanWrite == true)
            {
                if (speedMotor != motor)
                {
                    initialSpeed = (float)sprintSpeed.GetValue(motor);
                    initialHop = (float)hopSprintSpeed.GetValue(motor);
                    speedMotor = motor;
                }
                sprintSpeed.SetValue(motor, initialSpeed * multiplier);
                hopSprintSpeed.SetValue(motor, initialHop * multiplier);
                sprintStatus = "冲刺速度：" + multiplier + "×";
            }
            if (sprintEnabled && stamina?.CanWrite == true && staminaMax != null && exhausted?.CanWrite == true)
            {
                stamina.SetValue(motor, staminaMax.GetValue(motor));
                exhausted.SetValue(motor, false);
            }
        }
        catch (Exception e)
        {
            sprintEnabled = false;
            localRig = null;
            logger.LogWarning("Local sprint disabled: " + e.GetBaseException().Message);
        }
    }
}

public sealed class MoneyUpdate : MonoBehaviour
{
    public MoneyUpdate(IntPtr pointer) : base(pointer) {}
    public void Update() { MoneyPlugin.Tick(); }
    public void LateUpdate() { MoneyPlugin.RefreshLocalSprint(); }
}






