using System;

namespace HomeAutio.Mqtt.UnifiVideo
{
    /// <summary>
    /// <see cref="DateTime"/> extension methods.
    /// </summary>
    internal static class DateTimeExtensions
    {
        /// <summary>
        /// Removes milliseconds from a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="value"><see cref="DateTime"/> to modify.</param>
        /// <returns><see cref="DateTime"/> without milliseconds.</returns>
        public static DateTime RemoveMilliseconds(this DateTime value)
        {
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);
        }

        /// <summary>
        /// Convert a date time to a UTC unix timestamp.
        /// </summary>
        /// <param name="value">Value to convert.</param>
        /// <returns>UTC unix timestamp.</returns>
        public static long ToUnixTimestamp(this DateTime value)
        {
            return (long)value.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
