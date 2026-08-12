using System;
using System.Collections.Generic;
using System.Reflection;

namespace ErenshorFollow
{
    internal sealed class CampmasterControlBinding
    {
        private readonly PropertyInfo _isAvailable;
        private readonly PropertyInfo _isHuntCampActive;
        private readonly MethodInfo _tryDeclareHere;

        internal CampmasterControlBinding(PropertyInfo isAvailable, PropertyInfo isHuntCampActive, MethodInfo tryDeclareHere)
        {
            _isAvailable = isAvailable;
            _isHuntCampActive = isHuntCampActive;
            _tryDeclareHere = tryDeclareHere;
        }

        internal bool IsAvailable
        {
            get
            {
                try
                {
                    object value = _isAvailable.GetValue(null, null);
                    return value is bool && (bool)value;
                }
                catch { return false; }
            }
        }

        internal bool IsHuntCampActive
        {
            get
            {
                try
                {
                    object value = _isHuntCampActive.GetValue(null, null);
                    return value is bool && (bool)value;
                }
                catch { return false; }
            }
        }

        internal bool TryDeclareHere(out string failure)
        {
            failure = null;
            try
            {
                object[] args = new object[] { null };
                object result = _tryDeclareHere.Invoke(null, args);
                failure = args[0] as string;
                return result is bool && (bool)result;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                failure = "Campmaster handoff failed: " + inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                failure = "Campmaster handoff failed: " + ex.Message;
                return false;
            }
        }
    }

    internal static class CampmasterReflectionBinder
    {
        internal const string ControlTypeName = "ErenshorCampmaster.CampmasterControlApi";
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        internal static CampmasterControlBinding FindBinding(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null) return null;
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null) continue;
                Type type;
                try { type = assembly.GetType(ControlTypeName, false); }
                catch { continue; }
                CampmasterControlBinding binding = TryBindCandidate(type, true);
                if (binding != null) return binding;
            }
            return null;
        }

        internal static CampmasterControlBinding TryBindCandidate(Type type, bool requireCanonicalName)
        {
            if (type == null) return null;
            if (requireCanonicalName && !string.Equals(type.FullName, ControlTypeName, StringComparison.Ordinal)) return null;

            FieldInfo schema = type.GetField("SchemaVersion", PublicStatic);
            if (schema == null || schema.FieldType != typeof(int)) return null;
            int schemaVersion;
            try { schemaVersion = (int)schema.GetValue(null); }
            catch { return null; }
            if (schemaVersion != 1) return null;

            PropertyInfo isAvailable = ExactBoolProperty(type, "IsAvailable");
            PropertyInfo isActive = ExactBoolProperty(type, "IsHuntCampActive");
            MethodInfo request = ExactDeclareHere(type);
            if (isAvailable == null || isActive == null || request == null) return null;
            return new CampmasterControlBinding(isAvailable, isActive, request);
        }

        private static PropertyInfo ExactBoolProperty(Type type, string name)
        {
            PropertyInfo property;
            try { property = type.GetProperty(name, PublicStatic); }
            catch { return null; }
            if (property == null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0) return null;
            MethodInfo getter = property.GetGetMethod(false);
            return getter != null && getter.IsStatic && getter.IsPublic ? property : null;
        }

        private static MethodInfo ExactDeclareHere(Type type)
        {
            MethodInfo[] methods;
            try { methods = type.GetMethods(PublicStatic); }
            catch { return null; }
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "TryDeclareHere", StringComparison.Ordinal) || method.ReturnType != typeof(bool)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                if (parameters[0].ParameterType != typeof(string).MakeByRefType() || !parameters[0].IsOut) continue;
                return method;
            }
            return null;
        }
    }

    // Optional, public-member-only bridge. Follow has no compile-time dependency on Campmaster.
    internal static class CampmasterIntegrationBridge
    {
        private static readonly object Sync = new object();
        private static CampmasterControlBinding _binding;
        private static long _nextResolveUtcTicks;
        private const long RetryTicks = TimeSpan.TicksPerSecond * 2;

        internal static bool IsAvailable
        {
            get
            {
                CampmasterControlBinding binding = Resolve();
                return binding != null && binding.IsAvailable;
            }
        }

        internal static bool IsHuntCampActive
        {
            get
            {
                CampmasterControlBinding binding = Resolve();
                return binding != null && binding.IsAvailable && binding.IsHuntCampActive;
            }
        }

        internal static bool TryDeclareHere(out string failure)
        {
            CampmasterControlBinding binding = Resolve();
            if (binding == null || !binding.IsAvailable)
            {
                failure = "Erenshor Campmaster with the compatible control API is not available.";
                return false;
            }
            return binding.TryDeclareHere(out failure);
        }

        private static CampmasterControlBinding Resolve()
        {
            lock (Sync)
            {
                if (_binding != null) return _binding;
                long now = DateTime.UtcNow.Ticks;
                if (now < _nextResolveUtcTicks) return null;
                _nextResolveUtcTicks = now + RetryTicks;
                Assembly[] assemblies;
                try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
                catch { return null; }
                _binding = CampmasterReflectionBinder.FindBinding(assemblies);
                return _binding;
            }
        }
    }
}
