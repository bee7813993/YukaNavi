using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace YukaNavi.Core
{
    /// <summary>
    /// マイページ (履歴 / あとで歌う / お気に入り曲) の端末ローカル保存。
    /// スキンと同じ「端末内で完結」の方針で、サーバーには何も置かない。
    /// persistentDataPath/mypage.json に保存する。
    /// </summary>
    public static class LocalMypage
    {
        public class Item
        {
            [JsonProperty("fullpath")] public string FullPath;
            [JsonProperty("songfile")] public string Songfile;
            [JsonProperty("kind")] public string Kind;
            /// <summary>予約した回数 (履歴のみ)</summary>
            [JsonProperty("times")] public int Times;
            /// <summary>最後に予約した日時 unixtime (履歴のみ)</summary>
            [JsonProperty("last_at")] public long LastAt;
            /// <summary>追加日時 unixtime (あとで歌う / お気に入り)</summary>
            [JsonProperty("added_at")] public long AddedAt;
        }

        /// <summary>保存した検索 (キーワード or 完全一致条件)。検索トップのチップに並ぶ。</summary>
        public class SavedSearch
        {
            /// <summary>lister (あいまい) / everything (ファイル名) / exact (完全一致)</summary>
            [JsonProperty("kind")] public string Kind;
            /// <summary>キーワード (exact では空)</summary>
            [JsonProperty("keyword")] public string Keyword;
            /// <summary>exact のフィールド (program / artist / group / worker)</summary>
            [JsonProperty("field")] public string Field;
            /// <summary>exact の値</summary>
            [JsonProperty("value")] public string Value;
            /// <summary>チップ表示用ラベル</summary>
            [JsonProperty("label")] public string Label;
            [JsonProperty("added_at")] public long AddedAt;

            public bool SameCondition(SavedSearch other)
            {
                return Kind == other.Kind && Keyword == other.Keyword
                    && Field == other.Field && Value == other.Value;
            }
        }

        class Store
        {
            [JsonProperty("history")] public List<Item> History = new List<Item>();
            [JsonProperty("later")] public List<Item> Later = new List<Item>();
            [JsonProperty("favorite")] public List<Item> Favorite = new List<Item>();
            [JsonProperty("searches")] public List<SavedSearch> Searches = new List<SavedSearch>();
        }

        const int HistoryLimit = 500;

        static Store _store;

        static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "mypage.json"); }
        }

        static Store GetStore()
        {
            if (_store != null)
            {
                return _store;
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    _store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath));
                }
            }
            catch
            {
                // 壊れたファイルは作り直す
            }
            if (_store == null)
            {
                _store = new Store();
            }
            return _store;
        }

        static void Save()
        {
            try
            {
                File.WriteAllText(FilePath,
                    JsonConvert.SerializeObject(GetStore()), new UTF8Encoding(false));
            }
            catch
            {
                // 保存失敗はメモリ上のデータで動作継続
            }
        }

        static long Now()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // ---- 履歴 ----

        /// <summary>予約成功時に呼ぶ。同じ曲なら回数を増やす。</summary>
        public static void AddHistory(string fullpath, string songfile, string kind)
        {
            var store = GetStore();
            var item = store.History.Find(i => i.FullPath == fullpath);
            if (item != null)
            {
                item.Times++;
                item.LastAt = Now();
                item.Songfile = songfile;
            }
            else
            {
                store.History.Add(new Item
                {
                    FullPath = fullpath,
                    Songfile = songfile,
                    Kind = kind,
                    Times = 1,
                    LastAt = Now(),
                });
                if (store.History.Count > HistoryLimit)
                {
                    // 最終予約日時が最も古いものから削る
                    store.History.Sort((a, b) => a.LastAt.CompareTo(b.LastAt));
                    store.History.RemoveRange(0, store.History.Count - HistoryLimit);
                }
            }
            Save();
        }

        /// <summary>履歴 (最終予約日時の新しい順)。</summary>
        public static List<Item> GetHistory()
        {
            var list = new List<Item>(GetStore().History);
            list.Sort((a, b) => b.LastAt.CompareTo(a.LastAt));
            return list;
        }

        public static void RemoveHistory(string fullpath)
        {
            GetStore().History.RemoveAll(i => i.FullPath == fullpath);
            Save();
        }

        // ---- あとで歌う / お気に入り ----

        public static List<Item> GetLater()
        {
            var list = new List<Item>(GetStore().Later);
            list.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
            return list;
        }

        public static List<Item> GetFavorite()
        {
            var list = new List<Item>(GetStore().Favorite);
            list.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
            return list;
        }

        public static bool IsInLater(string fullpath)
        {
            return GetStore().Later.Exists(i => i.FullPath == fullpath);
        }

        public static bool IsInFavorite(string fullpath)
        {
            return GetStore().Favorite.Exists(i => i.FullPath == fullpath);
        }

        /// <summary>追加/解除をトグルする。追加されたら true。</summary>
        public static bool ToggleLater(string fullpath, string songfile, string kind)
        {
            return Toggle(GetStore().Later, fullpath, songfile, kind);
        }

        /// <summary>追加/解除をトグルする。追加されたら true。</summary>
        public static bool ToggleFavorite(string fullpath, string songfile, string kind)
        {
            return Toggle(GetStore().Favorite, fullpath, songfile, kind);
        }

        public static void RemoveLater(string fullpath)
        {
            GetStore().Later.RemoveAll(i => i.FullPath == fullpath);
            Save();
        }

        public static void RemoveFavorite(string fullpath)
        {
            GetStore().Favorite.RemoveAll(i => i.FullPath == fullpath);
            Save();
        }

        // ---- 保存した検索 ----

        /// <summary>保存した検索 (追加の新しい順)。</summary>
        public static List<SavedSearch> GetSavedSearches()
        {
            var list = new List<SavedSearch>(GetStore().Searches);
            list.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
            return list;
        }

        public static bool IsSavedSearch(SavedSearch search)
        {
            return GetStore().Searches.Exists(s => s.SameCondition(search));
        }

        /// <summary>保存/解除をトグルする。保存されたら true。</summary>
        public static bool ToggleSavedSearch(SavedSearch search)
        {
            var store = GetStore();
            int removed = store.Searches.RemoveAll(s => s.SameCondition(search));
            bool added = removed == 0;
            if (added)
            {
                search.AddedAt = Now();
                store.Searches.Add(search);
            }
            Save();
            return added;
        }

        static bool Toggle(List<Item> list, string fullpath, string songfile, string kind)
        {
            int removed = list.RemoveAll(i => i.FullPath == fullpath);
            bool added = removed == 0;
            if (added)
            {
                list.Add(new Item
                {
                    FullPath = fullpath,
                    Songfile = songfile,
                    Kind = kind,
                    AddedAt = Now(),
                });
            }
            Save();
            return added;
        }
    }
}
