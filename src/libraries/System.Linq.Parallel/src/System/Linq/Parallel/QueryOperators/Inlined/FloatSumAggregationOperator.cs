// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// =+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// FloatSumAggregationOperator.cs
//
// =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace System.Linq.Parallel
{
    /// <summary>
    /// An inlined sum aggregation and its enumerator, for floats.
    /// </summary>
    internal sealed class FloatSumAggregationOperator : InlinedAggregationOperator<float, double, float>
    {
        //---------------------------------------------------------------------------------------
        // Constructs a new instance of a sum associative operator.
        //

        internal FloatSumAggregationOperator(IEnumerable<float> child) : base(child)
        {
        }

        //---------------------------------------------------------------------------------------
        // Executes the entire query tree, and aggregates the intermediate results into the
        // final result based on the binary operators and final reduction.
        //
        // Return Value:
        //     The single result of aggregation.
        //

        protected override float InternalAggregate(ref Exception? singularExceptionToThrow)
        {
            // Because the final reduction is typically much cheaper than the intermediate
            // reductions over the individual partitions, and because each parallel partition
            // will do a lot of work to produce a single output element, we prefer to turn off
            // pipelining, and process the final reductions serially.
            using (IEnumerator<double> enumerator = GetEnumerator(ParallelMergeOptions.FullyBuffered, true))
            {
                // We just reduce the elements in each output partition.
                double sum = 0.0;
                while (enumerator.MoveNext())
                {
                    sum += enumerator.Current;
                }

                return (float)sum;
            }
        }

        //---------------------------------------------------------------------------------------
        // Creates an enumerator that is used internally for the final aggregation step.
        //

        protected override QueryOperatorEnumerator<double, int> CreateEnumerator<TKey>(
            int index, int count, QueryOperatorEnumerator<float, TKey> source, object? sharedData,
            CancellationToken cancellationToken)
        {
            return new FloatSumAggregationOperatorEnumerator<TKey>(source, index, cancellationToken);
        }

        //---------------------------------------------------------------------------------------
        // This enumerator type encapsulates the intermediary aggregation over the underlying
        // (possibly partitioned) data source.
        //

        private class FloatSumAggregationOperatorEnumerator<TKey> : InlinedAggregationOperatorEnumerator<double>
        {
            private readonly QueryOperatorEnumerator<float, TKey> _source; // The source data.

            //---------------------------------------------------------------------------------------
            // Instantiates a new aggregation operator.
            //

            internal FloatSumAggregationOperatorEnumerator(QueryOperatorEnumerator<float, TKey> source, int partitionIndex,
                CancellationToken cancellationToken) :
                base(partitionIndex, cancellationToken)
            {
                Debug.Assert(source != null);
                _source = source;
            }

            //---------------------------------------------------------------------------------------
            // Tallies up the sum of the underlying data source, walking the entire thing the first
            // time MoveNext is called on this object.
            //

            protected override bool MoveNextCore(ref double currentElement)
            {
                float element = default(float);
                TKey keyUnused = default(TKey)!;

                QueryOperatorEnumerator<float, TKey> source = _source;
                if (source.MoveNext(ref element, ref keyUnused))
                {
                    // We just scroll through the enumerator and accumulate the sum.
                    double tempSum = 0.0f;
                    int i = 0;
                    do
                    {
                        if ((i++ & CancellationState.POLL_INTERVAL) == 0)
                            CancellationState.ThrowIfCanceled(_cancellationToken);

                        tempSum += element;
                    }
                    while (source.MoveNext(ref element, ref keyUnused));

                    // The sum has been calculated. Now just return.
                    currentElement = tempSum;
                    return true;
                }

                return false;
            }

            //---------------------------------------------------------------------------------------
            // Dispose of resources associated with the underlying enumerator.
            //

            protected override void Dispose(bool disposing)
            {
                Debug.Assert(_source != null);
                _source.Dispose();
            }
        }
    }
}