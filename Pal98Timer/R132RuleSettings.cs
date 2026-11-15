// Refs othercat/sdlpal-lostedit#322-#335 / Pal98ConfigTool#98.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace Pal98.Challenges {
    internal sealed class R132Parameter {
        internal readonly string Rule,Slot,Field,Property,Label;
        internal readonly int Default,Maximum;
        internal R132Parameter(string rule,string slot,string field,string property,string label,int value,int maximum) {
            Rule=rule;Slot=slot;Field=field;Property=property;Label=label;Default=value;Maximum=maximum;
        }
        internal bool Valid(int value) => value>=1 && value<=Maximum;
        internal int Read(string text) {
            if(!Regex.IsMatch(text??"",@"\A[1-9][0-9]{0,4}\z") || !int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out int value) || !Valid(value))
                throw new InvalidDataException(Label+" 须为1至"+Maximum+"的整数。");
            return value;
        }
        internal int Value(object rules) => (int)rules.GetType().GetProperty(Field).GetValue(rules,null);
    }
    internal static class R132RuleSettings {
        internal static readonly string[] Keys = {"no_battle_items","no_death_benefits","freeze_hidden","minimum_throw","third_round_ghost","no_enemy_control","ban_gourd","ban_steal","closed_shops","no_hide","inventory_quantity","inventory_kinds","captain_illusion","ma35"};
        internal static readonly string[] Switches = {"NoBattleItems","NoDeathBenefits","FreezeHidden","MinimumThrow","ThirdRoundGhost","NoEnemyControl","BanGourd","BanSteal","ClosedShops","NoHide","InventoryQuantity","InventoryKinds","CaptainIllusion","Ma35"};
        internal static readonly R132Parameter[] Parameters = {
            new R132Parameter("closed_shops","ClosedShopsScope","closed_shops_scope","scope","关闭范围：1商店、2当铺、3二者",3,3),
            new R132Parameter("no_hide","NoHideWait","no_hide_wait","wait","原无隐等待次数 X",5,65535),
            new R132Parameter("inventory_quantity","InventoryQuantityLimit","inventory_quantity_limit","limit","背包单种数量 X（傀儡虫固定36）",3,99),
            new R132Parameter("inventory_kinds","InventoryKindsLimit","inventory_kinds_limit","limit","普通物品种类 X",35,256),
        };
        internal static Dictionary<string,string> NormalizeFacts(IDictionary<string,string> source) {
            var result=new Dictionary<string,string>(source,StringComparer.Ordinal);
            foreach(string slot in Switches)if(!result.TryGetValue(slot,out string enabled) || enabled=="0") {
                result.Remove(slot);result.Remove("Priority"+slot);
                foreach(var p in Parameters.Where(p=>Switches[Array.IndexOf(Keys,p.Rule)]==slot)) {result.Remove(p.Slot);result.Remove("Priority"+p.Slot);}
            }
            foreach(var p in Parameters)if(result.TryGetValue(Switches[Array.IndexOf(Keys,p.Rule)],out string enabled) && enabled=="1" && !result.ContainsKey(p.Slot))
                result[p.Slot]=p.Default.ToString(CultureInfo.InvariantCulture);
            return result;
        }
        internal static void ValidateSlot(string key,string value) {
            var p=Parameters.FirstOrDefault(field=>field.Slot==key);if(p!=null)p.Read(value);
            if(Switches.Contains(key) && value!="0" && value!="1")throw new InvalidDataException("规则开关须为0或1："+key);
        }
    }
}
