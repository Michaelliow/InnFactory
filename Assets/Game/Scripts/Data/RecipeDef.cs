using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    public class RecipeDef : ScriptableObject
    {
        public string id;
        public ItemDef[] ingredients;
        public CookMethod method;
        public ItemDef output;
        public float seconds;
        public bool startKnown;

        /// <summary>不分顺序的匹配键：方式 + 排序后的食材 id</summary>
        public static string Key(CookMethod m, IEnumerable<ItemDef> items)
        {
            var ids = new List<string>();
            foreach (var i in items) ids.Add(i.id);
            ids.Sort(StringComparer.Ordinal);
            return m + ":" + string.Join("+", ids);
        }
        public string Key() => Key(method, ingredients);

        public int CountOf(ItemDef item)
        {
            int n = 0;
            foreach (var i in ingredients) if (i == item) n++;
            return n;
        }

        public string Describe()
        {
            var names = new List<string>();
            foreach (var i in ingredients) names.Add(i.displayName);
            return string.Join(" + ", names) + "（" + Names.Of(method) + "）";
        }
    }
}
