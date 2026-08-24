using System;
using System.Reflection;

namespace ErenshorDuel
{
    // Optional, read-only PvP conflict check. It binds the current public PvpControlApi v1 shape
    // when present and fails closed only for a positively observed active/pending PvP encounter.
    internal static class DuelPvpCompatibility
    {
        internal static bool HasConflict()
        {
            try
            {
                Type api = FindType("ErenshorPvP.PvpControlApi");
                if (api == null) return false;
                MethodInfo get = api.GetMethod("GetBasicState", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (get == null) return false;
                object state = get.Invoke(null, null);
                if (state == null) return false;
                Type t = state.GetType();
                return ReadBool(t, state, "EncounterActive") || !string.IsNullOrWhiteSpace(ReadString(t, state, "PendingOpponent"));
            }
            catch { return false; }
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                try { Type t = assembly.GetType(name, false); if (t != null) return t; } catch { }
            return null;
        }

        private static bool ReadBool(Type t, object instance, string field)
        {
            try
            {
                FieldInfo f = t.GetField(field, BindingFlags.Public | BindingFlags.Instance);
                return f != null && Convert.ToBoolean(f.GetValue(instance));
            }
            catch { return false; }
        }

        private static string ReadString(Type t, object instance, string field)
        {
            try
            {
                FieldInfo f = t.GetField(field, BindingFlags.Public | BindingFlags.Instance);
                return f == null ? string.Empty : Convert.ToString(f.GetValue(instance)) ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
