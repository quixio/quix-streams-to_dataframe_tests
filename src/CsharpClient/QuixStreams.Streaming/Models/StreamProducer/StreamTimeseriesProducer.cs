﻿using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using QuixStreams.Streaming.Exceptions;
using QuixStreams.Telemetry.Managers;
using QuixStreams.Telemetry.Models.Utility;

namespace QuixStreams.Streaming.Models.StreamProducer
{
    /// <summary>
    /// Helper class for writing <see cref="ParameterDefinition"/> and <see cref="TimeseriesData"/>
    /// </summary>
    public class StreamTimeseriesProducer : IDisposable
    {
        private readonly IStreamProducerInternal streamProducer;

        private readonly ILogger logger = Logging.CreateLogger<StreamTimeseriesProducer>();

        private string location;
        private readonly ParameterDefinitionsManager parameterDefinitionsManager = new ParameterDefinitionsManager();
        private readonly Timer flushDefinitionsTimer;
        private bool timerEnabled = false; // Here because every now and then resetting its due time to never doesn't work
        private bool isDisposed;
        private const int TimerInterval = 20;
        private readonly object flushLock = new object();

        /// <summary>
        /// Initializes a new instance of <see cref="StreamTimeseriesProducer"/>
        /// </summary>
        /// <param name="topicProducer">The topic producer to publish to with</param>
        /// <param name="streamProducer">Stream writer owner</param>
        internal StreamTimeseriesProducer(ITopicProducer topicProducer, IStreamProducerInternal streamProducer)
        {
            this.streamProducer = streamProducer;

            // Parameters Buffer 
            this.Buffer = new TimeseriesBufferProducer(topicProducer, this.streamProducer, new TimeseriesBufferConfiguration());

            // Timer for Flush Parameter definitions
            flushDefinitionsTimer = new Timer(OnFlushDefinitionsTimerEvent, null, Timeout.Infinite, Timeout.Infinite); // Create disabled flush timer

            // Initialize root location
            this.DefaultLocation = "/";
        }

        /// <summary>
        /// Gets the buffer for writing timeseries data
        /// </summary>
        public TimeseriesBufferProducer Buffer { get;  }

        /// <summary>
        /// Write data to stream without using Buffer
        /// </summary>
        /// <param name="data">Timeseries data to write</param>
        public void Publish(TimeseriesData data)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }

            for (var index = 0; index < data.Timestamps.Count; index++)
            {
                var timestamp = data.Timestamps[index];

                if (!timestamp.EpochIncluded)
                {
                    timestamp.TimestampNanoseconds += this.streamProducer.Epoch.ToUnixNanoseconds();
                    timestamp.EpochIncluded = true;
                }
            }

            this.streamProducer.Publish(data.ConvertToTimeseriesDataRaw());
        }

        /// <summary>
        /// Write data timeseries data raw directly to stream
        /// </summary>
        /// <param name="data">Timeseries data to write</param>
        public void Publish(QuixStreams.Telemetry.Models.TimeseriesDataRaw data)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }

            long epochDiff = this.streamProducer.Epoch.ToUnixNanoseconds();

            if (epochDiff == 0)
            {
                // No epoch modification needed >> directly write to the stream
                this.streamProducer.Publish(data);
                return;
            }

            long[] updatedTimestamps = new long[data.Timestamps.Length];
            for (int i = 0; i < updatedTimestamps.Length; i++) 
            { 
                updatedTimestamps[i] = data.Timestamps[i] + epochDiff;
            }

            QuixStreams.Telemetry.Models.TimeseriesDataRaw new_data = new QuixStreams.Telemetry.Models.TimeseriesDataRaw(
                data.Epoch, 
                updatedTimestamps, 
                data.NumericValues, 
                data.StringValues, 
                data.BinaryValues, 
                data.TagValues
            );

            this.streamProducer.Publish(new_data);
        }


        /// <summary>
        /// Default Location of the parameters. Parameter definitions added with <see cref="AddDefinition"/> will be inserted at this location.
        /// See <see cref="AddLocation"/> for adding definitions at a different location without changing default.
        /// Example: "/Group1/SubGroup2"
        /// </summary>
        public string DefaultLocation
        {
            get
            {
                return this.location;
            }
            set
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
                }
                this.location = this.parameterDefinitionsManager.ReformatLocation(value);
            }
        }

        /// <summary>
        /// Adds a list of definitions to the <see cref="StreamTimeseriesProducer"/>. Configure it with the builder methods.
        /// </summary>
        /// <param name="definitions">List of definitions</param>
        public void AddDefinitions(List<ParameterDefinition> definitions)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }
            definitions.ForEach(d => this.parameterDefinitionsManager.AddDefinition(d.ConvertToTelemetrysDefinition(), d.Location));

            this.ResetFlushDefinitionsTimer();
        }

        /// <summary>
        /// Adds a new parameter definition to the <see cref="StreamTimeseriesProducer"/>. Configure it with the builder methods.
        /// </summary>
        /// <param name="parameterId">The id of the parameter. Must match the parameter id used to send data.</param>
        /// <param name="name">The human friendly display name of the parameter</param>
        /// <param name="description">The description of the parameter</param>
        /// <returns>Parameter definition builder to define the parameter properties</returns>
        public ParameterDefinitionBuilder AddDefinition(string parameterId, string name = null, string description = null)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }
            var parameterDefinition = this.CreateDefinition(this.location, parameterId, name, description);

            var builder = new ParameterDefinitionBuilder(this, this.location, parameterDefinition);

            return builder;
        }

        /// <summary>
        /// Adds a new location in the parameters groups hierarchy
        /// </summary>
        /// <param name="location">The group location</param>
        /// <returns>Parameter definition builder to define the parameters under the specified location</returns>
        public ParameterDefinitionBuilder AddLocation(string location)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }
            this.parameterDefinitionsManager.GenerateLocations(location);

            var builder = new ParameterDefinitionBuilder(this, location);

            return builder;
        }

        internal QuixStreams.Telemetry.Models.ParameterDefinition CreateDefinition(string location, string parameterId, string name, string description)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }
            var parameterDefinition = new QuixStreams.Telemetry.Models.ParameterDefinition
            {
                Id = parameterId,
                Name = name,
                Description = description
            };

            this.parameterDefinitionsManager.AddDefinition(parameterDefinition, location);

            this.ResetFlushDefinitionsTimer();

            return parameterDefinition;
        }

        /// <summary>
        /// Immediately writes the timeseries data and definitions from the buffer without waiting for buffer condition to fulfill for either
        /// </summary>
        public void Flush()
        {
            this.Flush(false);
        }

        private void Flush(bool force)
        {
            if (!force && isDisposed)
            {
                throw new ObjectDisposedException(nameof(StreamTimeseriesProducer));
            }

            try
            {
                lock (flushLock)
                {
                    this.FlushDefinitions();
                    this.Buffer.Flush();
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Exception occurred while trying to flush timeseries data buffer.");
            }
        }

        private void ResetFlushDefinitionsTimer()
        {
            if (isDisposed) return;
            timerEnabled = true;
            flushDefinitionsTimer.Change(TimerInterval, Timeout.Infinite); // Reset / Enable timer
        }

        private void OnFlushDefinitionsTimerEvent(object state)
        {
            if (!this.timerEnabled) return;
            try
            {
                this.FlushDefinitions();
            }
            catch (StreamClosedException exception) when (this.isDisposed)
            {
                // Ignore exception because the timer flush definition may finish executing only after closure due to how close lock works in streamProducer
            }
            catch (Exception ex)
            {
                this.logger.Log(LogLevel.Error, ex, "Exception occurred while trying to flush parameter definition buffer.");
            }
        }

        private void FlushDefinitions()
        {
            timerEnabled = false;
            flushDefinitionsTimer.Change(Timeout.Infinite, Timeout.Infinite); // Disable flush timer

            var definitions = parameterDefinitionsManager.GenerateParameterDefinitions();

            if (definitions.Parameters?.Count == 0 && definitions.ParameterGroups?.Count == 0) return; // there is nothing to flush

            this.streamProducer.Publish(definitions);
        }

        /// <summary>
        /// Flushes internal buffers and disposes
        /// </summary>
        public void Dispose()
        {
            if (this.isDisposed) return;
            this.isDisposed = true;
            this.Flush(true);
            flushDefinitionsTimer?.Dispose();
            Buffer?.Dispose();
        }
    }
}
