using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

public class MoneyPanel : Form
{
    readonly TextBox path = new TextBox();
    readonly NumericUpDown amount = new NumericUpDown();
    readonly Label status = new Label(), balance = new Label(), feedback = new Label();
    readonly Button add = new Button();
    readonly NumericUpDown speed = new NumericUpDown();
    readonly NumericUpDown height = new NumericUpDown();
    readonly ComboBox crew = new ComboBox();
    readonly Button drop = new Button(), sky = new Button();
    string roster = "";
    readonly Label speedStatus = new Label();
    readonly CheckBox sprint = new CheckBox();
    readonly Timer timer = new Timer();
    string session = "", pending = "";
    DateTime sent;
    string Folder { get { return Path.Combine(path.Text.Trim(), "BepInEx", "config", "MaydayMoneyControl"); } }
    public MoneyPanel()
    {
        Text = "MAYDAY AIRWAYS · 加钱工具";
        ClientSize = new Size(600, 635); Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var heading = new Label { Text="房主资金控制", Font=new Font(Font.FontFamily,17,FontStyle.Bold), Location=new Point(22,18), AutoSize=true };
        var hint = new Label { Text="先安装插件，再启动游戏并创建自己的房间。", Location=new Point(24,59), AutoSize=true };
        path.SetBounds(24,90,460,30); path.Text=Environment.GetEnvironmentVariable("MAYDAY_MONEY_GAME_PATH") ?? "";
        var browse = new Button { Text="选择目录", Location=new Point(490,88), Size=new Size(86,32) };
        browse.Click += delegate { using(var dlg=new FolderBrowserDialog()) { if(dlg.ShowDialog()==DialogResult.OK) path.Text=dlg.SelectedPath; } };
        status.SetBounds(24,134,550,28); status.Text="等待游戏插件连接…";
        balance.SetBounds(24,168,550,30); balance.Text="当前余额：—";
        amount.SetBounds(24,213,220,36); amount.Minimum=0; amount.Maximum=int.MaxValue; amount.Value=600; amount.ThousandsSeparator=true;
        add.SetBounds(264,210,170,38); add.Text="设为目标余额"; add.Enabled=false; add.Click+=Send;
        sprint.SetBounds(24,258,400,29); sprint.Text="无限冲刺（仅房主自己的角色）";
        sprint.Enabled=false; sprint.Click+=ToggleSprint;
        var speedLabel = new Label { Text="冲刺倍数", Location=new Point(24,304), AutoSize=true };
        speed.SetBounds(116,301,87,30); speed.Minimum=1; speed.Maximum=10; speed.DecimalPlaces=2; speed.Increment=0.25m; speed.Value=1;
        var apply = new Button {Text="应用",Location=new Point(214,299),Size=new Size(69,32)};
        var normal = new Button {Text="1×",Location=new Point(293,299),Size=new Size(65,32)};
        var doubleSpeed = new Button {Text="2×",Location=new Point(365,299),Size=new Size(65,32)};
        var quadSpeed = new Button {Text="4×",Location=new Point(437,299),Size=new Size(65,32)};
        apply.Click+=delegate { SetSpeed(speed.Value); };
        normal.Click+=delegate { speed.Value=1; SetSpeed(1); };
        doubleSpeed.Click+=delegate { speed.Value=2; SetSpeed(2); };
        quadSpeed.Click+=delegate { speed.Value=4; SetSpeed(4); };
        speedStatus.SetBounds(24,342,550,26);
        crew.SetBounds(24,375,290,30);
        crew.DropDownStyle=ComboBoxStyle.DropDownList;
        drop.SetBounds(323,373,115,32); drop.Text="全部掉落";
                height.SetBounds(310,414,80,30); height.Minimum=1; height.Maximum=1000; height.Value=15;
        var heightLabel=new Label {Text="传送高度（米）",Location=new Point(197,419),AutoSize=true};
        sky.SetBounds(447,373,129,32); sky.Text="向上传送";
        Controls.AddRange(new Control[]{heightLabel,height});
        drop.Enabled=sky.Enabled=false;
        drop.Click+=delegate { SendCrew("drop:"); };
        sky.Click+=delegate { SendCrew("sky:"); };
        Controls.AddRange(new Control[]{crew,drop,sky});
        var shoveLabel=new Label {Text="房主推力倍数",Location=new Point(24,456),AutoSize=true};
        var shove=new NumericUpDown {Minimum=1,Maximum=20,DecimalPlaces=2,Increment=0.25m,Value=1};
        shove.SetBounds(150,452,90,30);
        var shoveApply=new Button {Text="应用推力",Location=new Point(250,450),Size=new Size(98,32)};
        shoveApply.Click+=delegate {
            try {
                File.WriteAllText(Path.Combine(Folder,"shove-multiplier.txt"),shove.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                feedback.Text="已保存推力 " + shove.Value + "×。";
            } catch(Exception e) {feedback.Text="无法保存推力：" + e.Message;}
        };
        Controls.AddRange(new Control[]{shoveLabel,shove,shoveApply});
        var tile = new Button {Text="脚下布置弹簧地砖",Location=new Point(24,505),Size=new Size(190,34)};
        var cancel = new Button {Text="取消地砖",Location=new Point(225,505),Size=new Size(110,34)};
        tile.Click+=delegate { RefreshStatus(); if(add.Enabled) Submit("tile:arm"); else feedback.Text="请先进入自己的房间。"; };
        cancel.Click+=delegate { RefreshStatus(); if(add.Enabled) Submit("tile:cancel"); };
        Controls.AddRange(new Control[]{tile,cancel});
        feedback.SetBounds(24,570,550,55); feedback.Text="输入你想要的最终余额，再点「设为目标余额」。";
        Controls.AddRange(new Control[]{speedLabel,speed,apply,normal,doubleSpeed,quadSpeed,speedStatus});
        Controls.AddRange(new Control[]{heading,hint,path,browse,status,balance,amount,add,sprint,feedback});
        timer.Interval=500; timer.Tick+=delegate { RefreshStatus(); }; timer.Start();
        FormClosed+=delegate { timer.Dispose(); };
    }
    void RefreshStatus()
    {
        try
        {
            var lines=File.ReadAllLines(Path.Combine(Folder,"status.txt"));
            long ticks;
            if(lines.Length<6 || !long.TryParse(lines[1],out ticks) || (DateTime.UtcNow-new DateTime(ticks,DateTimeKind.Utc)).TotalSeconds>5)
                throw new IOException("插件离线");
            session=lines[0]; bool ready=lines[2]=="ready";
            status.Text=ready ? "已连接 · 你是房主" : lines[2]=="error" ? "插件报告错误" : "已连接 · 等待你创建房间";
            balance.Text=ready ? "当前余额："+lines[3] : "当前余额：—";
            if(pending.Length>0 && lines[4]==pending) { feedback.Text=lines[5]; pending=""; }
            else if(pending.Length>0 && (DateTime.UtcNow-sent).TotalSeconds>8) { feedback.Text="尚未收到确认，请核对游戏余额。不会自动重试。"; pending=""; }
            else if(lines[2]=="error") feedback.Text=lines[5];
            speedStatus.Text=ready && lines.Length>=7 ? lines[6] : "";
            add.Enabled=ready && lines.Length>=7 && pending.Length==0;
            var members = ready && lines.Length>7 ? lines[7..] : Array.Empty<string>();
            var signature=string.Join("\n",members);
            if(roster!=signature) {
                string selected=crew.SelectedItem as string;
                roster=signature; crew.Items.Clear(); crew.Items.AddRange(members);
                if(selected!=null && crew.Items.Contains(selected)) crew.SelectedItem=selected;
            }
            drop.Enabled=sky.Enabled=ready && members.Length>0 && pending.Length==0;
            sprint.Enabled=ready;
            sprint.Checked=File.Exists(Path.Combine(Folder,"unlimited-sprint.on"));
        }
        catch { drop.Enabled=sky.Enabled=false; add.Enabled=false; sprint.Enabled=false; status.Text="未连接 · 请启动安装了插件的游戏"; balance.Text="当前余额：—"; }
    }
    void SetSpeed(decimal value)
    {
        try {
            File.WriteAllText(Path.Combine(Folder,"sprint-multiplier.txt"), value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            feedback.Text="已保存冲刺倍数 " + value + "×。";
        }
        catch(Exception e) { feedback.Text="无法保存倍数：" + e.Message; }
    }
    void ToggleSprint(object sender, EventArgs args)
    {
        try
        {
            string marker=Path.Combine(Folder,"unlimited-sprint.on");
            if(sprint.Checked) File.WriteAllText(marker, "enabled");
            else File.Delete(marker);
        }
        catch(Exception e) { feedback.Text="无法保存冲刺设置："+e.Message; }
        RefreshStatus();
    }
    void Send(object sender, EventArgs args)
    {
        RefreshStatus(); if(!add.Enabled) return;
        Submit("set:"+((int)amount.Value).ToString());
    }
    void SendCrew(string action)
    {
        RefreshStatus();
        if (!drop.Enabled || !(crew.SelectedItem is string member)) { feedback.Text="请先选择目标成员。"; return; }
        string id=member.Split(':')[0];
        Submit(action+id+(action=="sky:" ? ":"+((int)height.Value).ToString() : ""));
    }
    void Submit(string command)
    {
        try
        {
            string request=Path.Combine(Folder,"request.txt");
            if(File.Exists(request)) { feedback.Text="已有请求等待处理，请稍后。"; return; }
            pending=Guid.NewGuid().ToString("N"); sent=DateTime.UtcNow;
            string temp=Path.Combine(Folder,pending+".tmp");
            File.WriteAllLines(temp,new[]{session,pending,command});
            File.Move(temp,request); add.Enabled=drop.Enabled=sky.Enabled=false; feedback.Text="已提交，等待游戏确认…";
        }
        catch(Exception e) { pending=""; feedback.Text="提交失败："+e.Message; }
    }
    [STAThread] public static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MoneyPanel()); }
}





