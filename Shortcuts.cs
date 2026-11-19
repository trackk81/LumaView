using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LumaView {
sealed class ShortcutAction {
    public readonly string Id;
    public readonly string Label;
    public readonly Keys DefaultKey;
    public ShortcutAction(string id,string label,Keys defaultKey){Id=id;Label=label;DefaultKey=defaultKey;}
}

static class Shortcuts {
    public static readonly ShortcutAction[] Actions={
        new ShortcutAction("details","상세 정보",Keys.Tab),
        new ShortcutAction("fullscreen","전체화면 전환",Keys.Alt|Keys.Enter),
        new ShortcutAction("explorer","탐색기 토글",Keys.Control|Keys.B),
        new ShortcutAction("open","파일 열기",Keys.Control|Keys.O),
        new ShortcutAction("folder","폴더 열기",Keys.Control|Keys.Shift|Keys.O),
        new ShortcutAction("prev","이전 이미지",Keys.Left),
        new ShortcutAction("next","다음 이미지",Keys.Right),
        new ShortcutAction("zoomIn","확대",Keys.Oemplus),
        new ShortcutAction("zoomOut","축소",Keys.OemMinus),
        new ShortcutAction("fit","화면 맞춤",Keys.F),
        new ShortcutAction("actualSize","100% 원본 크기",Keys.D1),
        new ShortcutAction("rotateRight","시계 방향 회전",Keys.R),
        new ShortcutAction("rotateLeft","반시계 방향 회전",Keys.Shift|Keys.R),
        new ShortcutAction("slideshow","슬라이드쇼",Keys.F5)
    };
    static readonly Dictionary<string,Keys> bindings=new Dictionary<string,Keys>(StringComparer.Ordinal);
    static Shortcuts(){ResetDefaults();}
    public static void ResetDefaults(){
        bindings.Clear();
        foreach(var action in Actions)bindings[action.Id]=action.DefaultKey;
    }
    public static Keys Get(string id){
        Keys key;
        return bindings.TryGetValue(id,out key)?key:Keys.None;
    }
    public static void Set(string id,Keys key){
        if(!bindings.ContainsKey(id))return;
        bindings[id]=Normalize(key);
    }
    public static Dictionary<string,Keys> Snapshot(){
        return new Dictionary<string,Keys>(bindings,StringComparer.Ordinal);
    }
    public static void Apply(Dictionary<string,Keys> values){
        if(values==null)return;
        foreach(var action in Actions){
            Keys key;
            if(values.TryGetValue(action.Id,out key))bindings[action.Id]=Normalize(key);
        }
    }
    public static bool Matches(string id,Keys key){
        Keys bound=Get(id);
        return bound!=Keys.None&&Normalize(key)==bound;
    }
    public static bool IsBound(Keys key){
        Keys normalized=Normalize(key);
        if(normalized==Keys.None)return false;
        foreach(var value in bindings.Values)if(value==normalized)return true;
        return false;
    }
    public static bool IsModifierOnly(Keys key){
        Keys code=key&Keys.KeyCode;
        return code==Keys.None||code==Keys.ControlKey||code==Keys.ShiftKey||code==Keys.Menu||
            code==Keys.LControlKey||code==Keys.RControlKey||code==Keys.LShiftKey||code==Keys.RShiftKey||
            code==Keys.LMenu||code==Keys.RMenu||code==Keys.LWin||code==Keys.RWin;
    }
    public static Keys Normalize(Keys key){
        Keys code=key&Keys.KeyCode;
        if(code==Keys.None)return Keys.None;
        Keys modifiers=key&(Keys.Control|Keys.Alt|Keys.Shift);
        return modifiers|code;
    }
    public static string Format(string id){return FormatKey(Get(id));}
    public static string FormatKey(Keys key){
        key=Normalize(key);
        if(key==Keys.None)return Ui.T("지정 안 됨");
        return KeyTokens(key);
    }
    public static string Serialize(Keys key){
        key=Normalize(key);
        return key==Keys.None?"None":KeyTokens(key);
    }
    static string KeyTokens(Keys key){
        var parts=new List<string>();
        if((key&Keys.Control)!=0)parts.Add("Ctrl");
        if((key&Keys.Alt)!=0)parts.Add("Alt");
        if((key&Keys.Shift)!=0)parts.Add("Shift");
        parts.Add(CodeName(key&Keys.KeyCode));
        return String.Join("+",parts);
    }
    static string CodeName(Keys code){
        if(code>=Keys.D0&&code<=Keys.D9)return ((int)(code-Keys.D0)).ToString();
        if(code>=Keys.NumPad0&&code<=Keys.NumPad9)return "Num"+((int)(code-Keys.NumPad0));
        switch(code){
            case Keys.Return:return "Enter";
            case Keys.Escape:return "Esc";
            case Keys.Back:return "Backspace";
            case Keys.Prior:return "PageUp";
            case Keys.Next:return "PageDown";
            case Keys.Oemplus:return "+";
            case Keys.OemMinus:return "-";
            case Keys.Add:return "Num+";
            case Keys.Subtract:return "Num-";
            case Keys.Multiply:return "Num*";
            case Keys.Divide:return "Num/";
            case Keys.Oemtilde:return "`";
            case Keys.OemOpenBrackets:return "[";
            case Keys.OemCloseBrackets:return "]";
            case Keys.OemPipe:return "\\";
            case Keys.OemSemicolon:return ";";
            case Keys.OemQuotes:return "'";
            case Keys.Oemcomma:return ",";
            case Keys.OemPeriod:return ".";
            case Keys.OemQuestion:return "/";
            default:return code.ToString();
        }
    }
    public static bool TryParse(string text,out Keys result){
        result=Keys.None;
        if(String.IsNullOrWhiteSpace(text))return false;
        string trimmed=text.Trim();
        if(String.Equals(trimmed,"None",StringComparison.OrdinalIgnoreCase))return true;
        Keys modifiers=Keys.None;
        var tokens=new List<string>();
        int start=0;
        for(int i=0;i<=trimmed.Length;i++){
            if(i==trimmed.Length||(trimmed[i]=='+'&&i>start)){
                tokens.Add(trimmed.Substring(start,i-start));
                start=i+1;
            }
        }
        if(tokens.Count==0)return false;
        for(int i=0;i<tokens.Count-1;i++){
            string mod=tokens[i].Trim();
            if(String.Equals(mod,"Ctrl",StringComparison.OrdinalIgnoreCase)||String.Equals(mod,"Control",StringComparison.OrdinalIgnoreCase))modifiers|=Keys.Control;
            else if(String.Equals(mod,"Alt",StringComparison.OrdinalIgnoreCase))modifiers|=Keys.Alt;
            else if(String.Equals(mod,"Shift",StringComparison.OrdinalIgnoreCase))modifiers|=Keys.Shift;
            else return false;
        }
        Keys code;
        if(!TryParseCode(tokens[tokens.Count-1].Trim(),out code))return false;
        result=Normalize(modifiers|code);
        return !IsModifierOnly(result);
    }
    static bool TryParseCode(string token,out Keys code){
        code=Keys.None;
        if(token.Length==1&&token[0]>='0'&&token[0]<='9'){code=Keys.D0+(token[0]-'0');return true;}
        if(token.Length==4&&token.StartsWith("Num",StringComparison.OrdinalIgnoreCase)&&token[3]>='0'&&token[3]<='9'){code=Keys.NumPad0+(token[3]-'0');return true;}
        switch(token){
            case "Enter":case "Return":code=Keys.Enter;return true;
            case "Esc":case "Escape":code=Keys.Escape;return true;
            case "Backspace":case "Back":code=Keys.Back;return true;
            case "PageUp":case "Prior":code=Keys.PageUp;return true;
            case "PageDown":case "Next":code=Keys.PageDown;return true;
            case "+":case "Oemplus":code=Keys.Oemplus;return true;
            case "-":case "OemMinus":code=Keys.OemMinus;return true;
            case "Num+":case "Add":code=Keys.Add;return true;
            case "Num-":case "Subtract":code=Keys.Subtract;return true;
            case "Num*":case "Multiply":code=Keys.Multiply;return true;
            case "Num/":case "Divide":code=Keys.Divide;return true;
            case "`":code=Keys.Oemtilde;return true;
            case "[":code=Keys.OemOpenBrackets;return true;
            case "]":code=Keys.OemCloseBrackets;return true;
            case "\\":code=Keys.OemPipe;return true;
            case ";":code=Keys.OemSemicolon;return true;
            case "'":code=Keys.OemQuotes;return true;
            case ",":code=Keys.Oemcomma;return true;
            case ".":code=Keys.OemPeriod;return true;
            case "/":code=Keys.OemQuestion;return true;
        }
        Keys parsed;
        if(Enum.TryParse(token,true,out parsed)&&(parsed&Keys.KeyCode)!=Keys.None&&!IsModifierOnly(parsed)){code=parsed&Keys.KeyCode;return true;}
        return false;
    }
    public static Dictionary<string,object> ToJsDescriptor(string id){
        Keys key=Get(id);
        if(key==Keys.None)return null;
        Keys code=key&Keys.KeyCode;
        string jsKey=null,jsCode=null;
        if(code>=Keys.A&&code<=Keys.Z){jsKey=code.ToString().ToLowerInvariant();jsCode="Key"+code.ToString();}
        else if(code>=Keys.D0&&code<=Keys.D9){int d=(int)(code-Keys.D0);jsKey=d.ToString();jsCode="Digit"+d;}
        else if(code>=Keys.NumPad0&&code<=Keys.NumPad9){int d=(int)(code-Keys.NumPad0);jsCode="Numpad"+d;}
        else if(code>=Keys.F1&&code<=Keys.F12){jsKey=code.ToString();jsCode=code.ToString();}
        else switch(code){
            case Keys.Tab:jsKey="Tab";jsCode="Tab";break;
            case Keys.Enter:jsKey="Enter";jsCode="Enter";break;
            case Keys.Space:jsKey=" ";jsCode="Space";break;
            case Keys.Back:jsKey="Backspace";jsCode="Backspace";break;
            case Keys.Delete:jsKey="Delete";jsCode="Delete";break;
            case Keys.Insert:jsKey="Insert";jsCode="Insert";break;
            case Keys.Home:jsKey="Home";jsCode="Home";break;
            case Keys.End:jsKey="End";jsCode="End";break;
            case Keys.PageUp:jsKey="PageUp";jsCode="PageUp";break;
            case Keys.PageDown:jsKey="PageDown";jsCode="PageDown";break;
            case Keys.Left:jsKey="ArrowLeft";jsCode="ArrowLeft";break;
            case Keys.Right:jsKey="ArrowRight";jsCode="ArrowRight";break;
            case Keys.Up:jsKey="ArrowUp";jsCode="ArrowUp";break;
            case Keys.Down:jsKey="ArrowDown";jsCode="ArrowDown";break;
            case Keys.Oemplus:jsKey="=";jsCode="Equal";break;
            case Keys.OemMinus:jsKey="-";jsCode="Minus";break;
            case Keys.Add:jsKey="+";jsCode="NumpadAdd";break;
            case Keys.Subtract:jsKey="-";jsCode="NumpadSubtract";break;
            case Keys.Oemtilde:jsKey="`";jsCode="Backquote";break;
            case Keys.OemOpenBrackets:jsKey="[";jsCode="BracketLeft";break;
            case Keys.OemCloseBrackets:jsKey="]";jsCode="BracketRight";break;
            case Keys.OemPipe:jsKey="\\";jsCode="Backslash";break;
            case Keys.OemSemicolon:jsKey=";";jsCode="Semicolon";break;
            case Keys.OemQuotes:jsKey="'";jsCode="Quote";break;
            case Keys.Oemcomma:jsKey=",";jsCode="Comma";break;
            case Keys.OemPeriod:jsKey=".";jsCode="Period";break;
            case Keys.OemQuestion:jsKey="/";jsCode="Slash";break;
            default:jsKey=code.ToString();break;
        }
        return new Dictionary<string,object>{
            {"ctrl",(key&Keys.Control)!=0},
            {"alt",(key&Keys.Alt)!=0},
            {"shift",(key&Keys.Shift)!=0},
            {"key",jsKey},
            {"code",jsCode}
        };
    }
}

sealed class ShortcutSettings:Panel {
    public readonly Dictionary<string,Keys> Draft=Shortcuts.Snapshot();
    readonly Dictionary<string,ModernButton> buttons=new Dictionary<string,ModernButton>(StringComparer.Ordinal);
    readonly WrapLabel status;
    string capturingId;
    public bool IsCapturing {get{return capturingId!=null;}}
    public ShortcutSettings(){
        Dock=DockStyle.Fill;AutoScroll=true;Padding=new Padding(24,20,24,20);
        var stack=new SettingsStack{Dock=DockStyle.Top};
        stack.Controls.Add(ModernUi.Text(Ui.T("단축키"),false,21,true));
        stack.Controls.Add(ModernUi.Text(ComponentSetup.Say("항목 버튼을 클릭한 뒤 원하는 키를 눌러 단축키를 지정하세요. (Esc: 취소)","Click a shortcut button, then press a key to assign it. (Esc: cancel)"),true));
        status=ModernUi.Text("",true);status.Name="shortcutStatus";status.Visible=false;
        stack.Controls.Add(status);
        var card=new ModernCard();
        foreach(var action in Shortcuts.Actions){
            var current=action;
            var label=ModernUi.Text(Ui.T(current.Label));
            var button=new ModernButton{Name="shortcut_"+current.Id,MinimumSize=new Size(156,38),Margin=Padding.Empty};
            button.Click+=delegate{ToggleCapture(current.Id);};
            button.LostFocus+=delegate{if(capturingId==current.Id)CancelCapture();};
            buttons[current.Id]=button;
            card.Controls.Add(new SettingsRow(label,button));
        }
        var reset=new ModernButton{Text=Ui.T("기본값으로 복원"),Name="shortcutReset"};
        reset.Click+=delegate{
            CancelCapture();
            foreach(var action in Shortcuts.Actions)Draft[action.Id]=action.DefaultKey;
            RefreshButtons();
            SetStatus(ComponentSetup.Say("모든 단축키를 기본값으로 복원했습니다. 저장을 누르면 적용됩니다.","Restored all default shortcuts. Save to apply."));
        };
        card.Controls.Add(ModernUi.Actions(reset));
        stack.Controls.Add(card);
        Controls.Add(stack);
        RefreshButtons();
    }
    void SetStatus(string message){
        status.Text=message??"";
        status.Visible=!String.IsNullOrEmpty(status.Text);
    }
    void ToggleCapture(string id){
        if(capturingId==id){CancelCapture();return;}
        capturingId=id;
        RefreshButtons();
        var action=Shortcuts.Actions.First(a=>a.Id==id);
        SetStatus(Ui.Language=="en"
            ?"Press a key for \""+Ui.T(action.Label)+"\" (Esc to cancel)."
            :"'"+Ui.T(action.Label)+"'에 지정할 키를 누르세요. (Esc: 취소)");
        buttons[id].Focus();
    }
    public void CancelCapture(){
        if(capturingId==null)return;
        capturingId=null;
        RefreshButtons();
        SetStatus("");
    }
    public bool CaptureKey(Keys key){
        if(capturingId==null)return false;
        if(key==Keys.Escape){CancelCapture();return true;}
        if(Shortcuts.IsModifierOnly(key))return true;
        Keys normalized=Shortcuts.Normalize(key);
        string targetId=capturingId;
        string clearedLabel=null;
        foreach(var action in Shortcuts.Actions){
            if(action.Id!=targetId&&Draft[action.Id]==normalized){
                Draft[action.Id]=Keys.None;
                clearedLabel=Ui.T(action.Label);
            }
        }
        Draft[targetId]=normalized;
        capturingId=null;
        RefreshButtons();
        var target=Shortcuts.Actions.First(a=>a.Id==targetId);
        string keyName=Shortcuts.FormatKey(normalized);
        if(clearedLabel!=null){
            SetStatus(Ui.Language=="en"
                ?keyName+" assigned to \""+Ui.T(target.Label)+"\" (cleared from \""+clearedLabel+"\")."
                :keyName+" 키를 '"+Ui.T(target.Label)+"'에 지정했습니다. ('"+clearedLabel+"'에서 해제됨)");
        }else{
            SetStatus(Ui.Language=="en"
                ?keyName+" assigned to \""+Ui.T(target.Label)+"\"."
                :keyName+" 키를 '"+Ui.T(target.Label)+"'에 지정했습니다.");
        }
        return true;
    }
    public void RefreshButtons(){
        foreach(var action in Shortcuts.Actions){
            ModernButton button;
            if(!buttons.TryGetValue(action.Id,out button))continue;
            bool active=capturingId==action.Id;
            button.Selected=active;
            button.Text=active?Ui.T("키 입력 대기 중…"):Shortcuts.FormatKey(Draft[action.Id]);
            button.Invalidate();
        }
    }
}
}
