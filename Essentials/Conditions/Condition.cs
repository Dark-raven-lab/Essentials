using System;
using System.Reflection;
using Sandbox.Game.Entities;
using Torch.Commands;

namespace Essentials.Conditions
{
    public class Condition
    {
        public string Command;
        public string InvertCommand;
        public string HelpText;
        private MethodInfo _method;
        public readonly ParameterInfo Parameter;

        public Condition(MethodInfo evalMethod, ConditionAttribute attribute)
        {
            Command = attribute.Command;
            InvertCommand = attribute.InvertCommand;
            HelpText = attribute.HelpText;
            _method = evalMethod;
            if (_method.ReturnType != typeof(bool))
                throw new TypeLoadException("Condition does not return a bool!");
            var p = _method.GetParameters();
            if (p.Length < 1 || p[0].ParameterType != typeof(MyCubeGrid))
                throw new TypeLoadException("Condition does not accept MyCubeGrid as first parameter");
            if (p.Length > 2)
                throw new TypeLoadException("Condition can only have two parameters");
            if (p.Length == 1)
                Parameter = null;
            else
                Parameter = p[1];
        }

        public bool Matches(string name)
        {
            return name.Equals(Command, StringComparison.CurrentCultureIgnoreCase)
                   || name.Equals(InvertCommand, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// Checks and parses the argument once, before any grid is scanned.
        /// Returns null and sets the error if the argument doesn't fit.
        /// </summary>
        public Func<MyCubeGrid, bool> Bind(string arg, bool invert, out string error)
        {
            error = null;
            var hasArg = !string.IsNullOrEmpty(arg);
            if (Parameter == null)
            {
                if (hasArg)
                {
                    error = $"Условие '{Command}' не принимает аргумент '{arg}'.";
                    return null;
                }

                return grid => (bool)_method.Invoke(null, new object[] { grid }) != invert;
            }

            object val;
            if (hasArg)
            {
                if (!TryParseArgument(arg, out val, out error))
                    return null;
            }
            else if (Parameter.HasDefaultValue)
            {
                val = Parameter.DefaultValue;
            }
            else
            {
                error = $"Условию '{Command}' нужен аргумент {Parameter.Name}.";
                return null;
            }

            return grid => (bool)_method.Invoke(null, new[] { grid, val }) != invert;
        }

        // Types built from a single string (Regex, OwnerFilter) validate themselves in the constructor.
        private bool TryParseArgument(string arg, out object val, out string error)
        {
            error = null;
            val = null;
            try
            {
                var constructor = Parameter.ParameterType.GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    val = constructor.Invoke(new object[] { arg });
                    return true;
                }

                // Torch's TryConvert throws on a bad value instead of returning false.
                if (arg.TryConvert(Parameter.ParameterType, out val))
                    return true;
            }
            catch (Exception e)
            {
                var reason = e is TargetInvocationException ? e.InnerException?.Message : e.Message;
                error = $"Не удалось разобрать аргумент '{arg}' условия '{Command}': {reason}";
                return false;
            }

            error = $"Не удалось разобрать аргумент '{arg}' условия '{Command}'.";
            return false;
        }
    }
}