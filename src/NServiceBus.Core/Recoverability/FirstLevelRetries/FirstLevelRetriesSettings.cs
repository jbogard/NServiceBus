namespace NServiceBus
{
    /// <summary>
    /// Configuration settings for first level retries.
    /// </summary>
    public class FirstLevelRetriesSettings
    {
        EndpointConfiguration config;

        internal FirstLevelRetriesSettings(EndpointConfiguration config)
        {
            this.config = config;
        }

        /// <summary>
        /// Configures the amount of times a message should be immediately retried after failing before escalating to second level retries.
        /// </summary>
        /// <param name="numberOfRetries">The number of times to immediately retry a failed message.</param>
        public FirstLevelRetriesSettings NumberOfRetries(int numberOfRetries)
        {
            Guard.AgainstNegative(nameof(numberOfRetries), numberOfRetries);
            config.Settings.Set(Recoverability.FlrNumberOfRetries, numberOfRetries);

            return this;
        }

        /// <summary>
        /// Configures NServiceBus to not retry failed messages using the first level retry mechanism.
        /// </summary>
        public FirstLevelRetriesSettings Disable()
        {
            config.Settings.Set(Recoverability.FlrNumberOfRetries, 0);

            return this;
        }
    }
}