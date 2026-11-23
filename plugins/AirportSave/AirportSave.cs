using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using Mayday;

[BepInPlugin("local.mayday.airportsave","Airport Save Trial","0.3.3")]
public sealed class AirportSave : BasePlugin {
    static readonly string[] fields = "RunSeed Leg CurrentAirport CurrentAirportName DestinationAirport DestinationName Money Debt Rating Difficulty TimeOfDay Weather TotalPassengers TotalEarned GroundTimeLeft LegTime".Split(' ');
    public sealed class Slot {
        public string Id {get;set;}
        public float Amount {get;set;}
        public int Count {get;set;}
    }
    static PlayerInventory HostInventory() {
        var host=PlayerNet.Local;
        if(host==null || host.Inventory==null) throw new InvalidOperationException("房主物品欄尚未就緒。");
        return host.Inventory;
    }
    static Slot[] CaptureInventory() {
        var inventory=HostInventory();
        var slots=new Slot[PlayerInventory.SlotCount];
        for(int i=0;i<slots.Length;i++) {
            var item=inventory.Get(i);
            if(item!=null) slots[i]=new Slot {Id=item.ItemId,Amount=item.NetworkAmount,Count=item.NetworkCount};
        }
        return slots;
    }
    static void RestoreInventory(Slot[] slots) {
        var inventory=HostInventory();
        if(slots==null || slots.Length!=PlayerInventory.SlotCount) throw new InvalidDataException("物品欄格數不符。");
        Item starterHammer=null;
        var missing=new List<(int Index, Slot Value)>();
        for(int i=0;i<slots.Length;i++) {
            var saved=slots[i]; var current=inventory.Get(i);
            if(current!=null) {
                if(i==0 && saved==null && current.ItemId=="hammer" && current.NetworkAmount==0 && current.NetworkCount==0 && Enumerable.Range(1,slots.Length-1).All(n=>inventory.Get(n)==null)) {
                    starterHammer=current;
                    continue;
                }
                if(saved==null || current.ItemId!=saved.Id || current.NetworkAmount!=saved.Amount || current.NetworkCount!=saved.Count)
                    throw new InvalidOperationException("物品欄已有不同物品，請用新房間讀檔。");
            } else if(saved!=null) {
                if(string.IsNullOrWhiteSpace(saved.Id) || !float.IsFinite(saved.Amount) || saved.Count<0)
                    throw new InvalidDataException("無效的道具資料。");
                var info=ItemCatalog.Get(saved.Id);
                if(info==null || info.bulky) throw new InvalidOperationException("此道具無法自動補回："+saved.Id);
                missing.Add((i,saved));
            }
        }
        var created=new List<Item>();
        int activeSlot=inventory.Networkactive;
        try {
            foreach(var entry in missing) {
                var saved=entry.Value;
                inventory.Networkactive=entry.Index;
                var item=inventory.ServerGive(saved.Id,null);
                if(item==null) throw new InvalidOperationException("無法補回 "+saved.Id);
                created.Add(item);
                if(inventory.Get(entry.Index)?.netId!=item.netId) throw new InvalidOperationException("遊戲未把道具放回原格，已取消補回。");
                item.NetworkAmount=saved.Amount;
                item.NetworkCount=saved.Count;
            }
        } catch {
            foreach(var item in created) if(item!=null) item.ServerDestroy();
            throw;
        } finally {inventory.Networkactive=activeSlot;}
        if(starterHammer!=null) starterHammer.ServerDestroy();
    }
    public sealed class CabinItem {
        public string Id {get;set;}
        public float Amount {get;set;}
        public int Count {get;set;}
        public uint Tag {get;set;}
        public byte[] Tint {get;set;}
        public float[] Pose {get;set;}
    }
    static Item[] CabinItems() {
        return UnityEngine.Object.FindObjectsOfType<Item>()
            .Where(item=>item!=null && item.IsFree && !item.IsHeld && !item.InSocket && Mayday.UI.TutorialFlow.InsidePlane(item.transform.position))
            .ToArray();
    }
    static void DescribeCabinCandidates(){
        var world=WorldRoot.Instance;
        var rows=new List<string>{"id,free,inWorld,held,inSocket,insidePlane,planeLocalX,planeLocalY,planeLocalZ"};
        foreach(var item in UnityEngine.Object.FindObjectsOfType<Item>()){
            if(item==null)continue;
            var pos=item.transform.position;
            var local=Quaternion.Inverse(world.PlaneRot)*(pos-world.PlanePos);
            rows.Add(FormattableString.Invariant($"{item.ItemId},{item.IsFree},{item.InWorld},{item.IsHeld},{item.InSocket},{Mayday.UI.TutorialFlow.InsidePlane(pos)},{local.x:F2},{local.y:F2},{local.z:F2}"));
        }
        File.WriteAllLines(Path.Combine(folder,"cabin-diagnostics.csv"),rows);
    }
    static CabinItem[] CaptureCabinItems() {
        var root=WorldRoot.Instance;
        var inverse=Quaternion.Inverse(root.PlaneRot);
        return CabinItems().Select(item=>{
            var pos=inverse*(item.transform.position-root.PlanePos);
            var rot=inverse*item.transform.rotation;
            var tint=item.NetworkTint;
            return new CabinItem {Id=item.ItemId,Amount=item.NetworkAmount,Count=item.NetworkCount,Tag=item.NetworkTag,
                Tint=new[]{tint.r,tint.g,tint.b,tint.a},Pose=new[]{pos.x,pos.y,pos.z,rot.x,rot.y,rot.z,rot.w}};
        }).ToArray();
    }
    static void RestoreCabinItems(CabinItem[] saved,List<Item> spawned) {
        var root=WorldRoot.Instance;
        var existing=CabinItems().ToList();
        foreach(var entry in saved) {
            if(entry==null || string.IsNullOrWhiteSpace(entry.Id) || ItemCatalog.Get(entry.Id)==null ||
                !float.IsFinite(entry.Amount) || entry.Pose==null || entry.Pose.Length!=7 ||
                entry.Pose.Any(x=>!float.IsFinite(x)) || entry.Tint==null || entry.Tint.Length!=4)
                throw new InvalidDataException("機艙道具資料無效。");
            var p=entry.Pose;
            var position=root.PlanePos+root.PlaneRot*new Vector3(p[0],p[1],p[2]);
            if(!Mayday.UI.TutorialFlow.InsidePlane(position)) throw new InvalidDataException("道具位置不在機艙內。");
            var match=existing.Find(i=>i.ItemId==entry.Id && i.NetworkAmount==entry.Amount && i.NetworkCount==entry.Count &&
                (i.transform.position-position).sqrMagnitude<0.04f);
            if(match!=null){existing.Remove(match);continue;}
            var item=Item.ServerSpawn(entry.Id,position,root.PlaneRot*new Quaternion(p[3],p[4],p[5],p[6]),null,false);
            if(item==null) throw new InvalidOperationException("無法恢復機艙道具 "+entry.Id);
            spawned.Add(item);
            item.NetworkAmount=entry.Amount;
            item.NetworkCount=entry.Count;
            item.NetworkTag=entry.Tag;
            item.NetworkTint=new Color32(entry.Tint[0],entry.Tint[1],entry.Tint[2],entry.Tint[3]);
        }
    }
    static string[] CaptureUpgrades(GameState state) {
        var keys=new string[state.Upgrades.Count];
        for(int i=0;i<keys.Length;i++)keys[i]=state.Upgrades[i];
        return keys;
    }
    static void RestoreUpgrades(GameState state,string[] keys) {
        foreach(var group in keys.GroupBy(k=>k)) {
            int target=group.Count();
            while(state.UpgradeLevel(group.Key)<target) {
                int before=state.UpgradeLevel(group.Key);
                state.ServerAddUpgrade(group.Key);
                if(state.UpgradeLevel(group.Key)<=before)throw new InvalidOperationException("無法恢復升級 "+group.Key);
            }
        }
    }
    static long next;
    static string folder;
    static Type server, game;
    static BepInEx.Logging.ManualLogSource log;
    public override void Load() {
        log=Log;
        folder=Path.Combine(Paths.ConfigPath,"AirportSave");
        Directory.CreateDirectory(folder);
        server=Assembly.Load("Mirror").GetType("Mirror.NetworkServer",true);
        game=Assembly.Load("Assembly-CSharp").GetType("Mayday.GameState",true);
        AddComponent<AirportTick>();
    }
    static object Active() {
        return server.GetProperty("active")?.GetValue(null) ?? server.GetField("active")?.GetValue(null);
    }
    public static void Poll() {
        if(Environment.TickCount64<next) return;
        next=Environment.TickCount64+500;
        string save=Path.Combine(folder,"save.request"), load=Path.Combine(folder,"load.request");
        bool writing=File.Exists(save);
        if(!writing && !File.Exists(load)) return;
        try {
            File.Delete(writing?save:load);
            var state=game.GetProperty("Instance").GetValue(null);
            string phase=state==null?"":game.GetProperty("NetworkPhase").GetValue(state).ToString();
            if(!Equals(Active(),true) || (phase!="Ground" && phase!="PreShift"))
                throw new InvalidOperationException("请在房主的机场地面阶段操作。");
            var props=fields.Select(k=>game.GetProperty("Network"+k)).ToArray();
            var old=props.Select(p=>p.GetValue(state)).ToArray();
            if(writing) {
                try { DescribeCabinCandidates(); } catch(Exception e) { log.LogWarning("Cabin diagnostics: "+e.GetBaseException().Message); }
                var data=new Dictionary<string,object>(); for(int n=0;n<fields.Length;n++) data.Add(fields[n],old[n]);
                data.Add("Phase",phase); data.Add("HostInventory",CaptureInventory()); data.Add("CabinItems",CaptureCabinItems()); data.Add("Upgrades",CaptureUpgrades((GameState)state));
                string file=Path.Combine(folder,"airport-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");
                File.WriteAllText(file+".tmp",JsonSerializer.Serialize(data)); File.Move(file+".tmp",file);
                Report("已保存 "+Path.GetFileName(file));
            } else {
                string file=Directory.GetFiles(folder,"airport-*.json").OrderByDescending(x=>x,StringComparer.Ordinal).First();
                using var doc=JsonDocument.Parse(File.ReadAllText(file));
                var values=fields.Select((k,i)=>JsonSerializer.Deserialize(doc.RootElement.GetProperty(k).GetRawText(),props[i].PropertyType)).ToArray();
                if(doc.RootElement.TryGetProperty("Phase",out var savedPhase) && savedPhase.GetString()!=phase) throw new InvalidOperationException("請在與存檔相同的遊戲階段讀取。");
                var spawned=new List<Item>();
                int i=0;
                try { for(;i<props.Length;i++) props[i].SetValue(state,values[i]);
                    if(doc.RootElement.TryGetProperty("CabinItems",out var cabin)) RestoreCabinItems(JsonSerializer.Deserialize<CabinItem[]>(cabin.GetRawText()),spawned);
                    if(doc.RootElement.TryGetProperty("HostInventory",out var inv)) RestoreInventory(JsonSerializer.Deserialize<Slot[]>(inv.GetRawText())); }
                catch { foreach(var item in spawned) if(item!=null) item.ServerDestroy(); for(int j=Math.Min(i,props.Length-1);j>=0;j--) props[j].SetValue(state,old[j]); throw; }
                if(doc.RootElement.TryGetProperty("Upgrades",out var upgrades)) RestoreUpgrades((GameState)state,JsonSerializer.Deserialize<string[]>(upgrades.GetRawText()));
                Report("已读回地面进度字段 "+Path.GetFileName(file));
            }
        } catch(Exception e) { Report("操作失败 "+e.GetBaseException().Message); }
    }
    static void Report(string message){ log.LogInfo(message); File.WriteAllText(Path.Combine(folder,"result.txt"),message); }
}
public sealed class AirportTick : MonoBehaviour {
    public AirportTick(IntPtr p):base(p){}
    public void Update(){AirportSave.Poll();}
}








