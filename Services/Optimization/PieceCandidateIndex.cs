// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/PieceCandidateIndex.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — Indexed Spatial & Dimensional Candidate Retrieval
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// High-performance dimensional candidate index for guillotine optimization.
    /// Eliminates O(N^2) linear scans across remaining pieces by binning pieces by
    /// dimension buckets and area, while preventing piece starvation via aging/cursor rotation.
    /// </summary>
    public sealed class PieceCandidateIndex
    {
        private readonly List<CutPiece> _allPieces;
        private readonly bool[] _placed;
        private readonly List<int> _activeIndices;
        private int _remainingCount;
        private int _queryCursor;

        // Bucketed by max dimension in 100mm increments
        private const int BucketStepMm = 100;
        private const int MaxBuckets = 50; // up to 5000mm
        private readonly List<int>[] _buckets;

        public int RemainingCount => _remainingCount;

        public PieceCandidateIndex(IReadOnlyList<CutPiece> pieces)
        {
            _allPieces = new List<CutPiece>(pieces.Count);
            _placed = new bool[pieces.Count];
            _activeIndices = new List<int>(pieces.Count);
            _remainingCount = pieces.Count;
            _queryCursor = 0;

            _buckets = new List<int>[MaxBuckets];
            for (int i = 0; i < MaxBuckets; i++)
                _buckets[i] = new List<int>(pieces.Count / 10 + 16);

            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                _allPieces.Add(p);
                _activeIndices.Add(i);

                int maxDim = Math.Max(p.WidthMm, p.HeightMm);
                int bIdx = Math.Min(MaxBuckets - 1, Math.Max(0, maxDim / BucketStepMm));
                _buckets[bIdx].Add(i);
            }
        }

        /// <summary>
        /// Marks a piece index as placed, removing it from active consideration.
        /// </summary>
        public void MarkPlaced(int originalIndex)
        {
            if (originalIndex >= 0 && originalIndex < _placed.Length && !_placed[originalIndex])
            {
                _placed[originalIndex] = true;
                _remainingCount--;
            }
        }

        /// <summary>
        /// Retrieves bounded candidate pieces that plausibly fit within (maxWidth, maxHeight).
        /// Incorporates starvation-prevention cursor rotation across calls.
        /// </summary>
        public List<(int Index, CutPiece Piece)> QueryFittingCandidates(
            int maxWidth,
            int maxHeight,
            bool allowRotation,
            int maxCandidates = 100)
        {
            var results = new List<(int, CutPiece)>(Math.Min(maxCandidates, _remainingCount));
            if (_remainingCount == 0 || maxWidth <= 0 || maxHeight <= 0) return results;

            int targetLimit = Math.Max(10, maxCandidates);
            int maxAllowedDim = allowRotation ? Math.Max(maxWidth, maxHeight) : Math.Max(maxWidth, maxHeight);
            int maxBucketIdx = Math.Min(MaxBuckets - 1, maxAllowedDim / BucketStepMm);

            // 1. Scan appropriate dimensional buckets
            for (int b = maxBucketIdx; b >= 0 && results.Count < targetLimit; b--)
            {
                var bucket = _buckets[b];
                int bCount = bucket.Count;
                if (bCount == 0) continue;

                // Start from cursor to guarantee starvation prevention
                int startOffset = _queryCursor % bCount;
                for (int step = 0; step < bCount && results.Count < targetLimit; step++)
                {
                    int itemIdx = (startOffset + step) % bCount;
                    int pIdx = bucket[itemIdx];
                    if (_placed[pIdx]) continue;

                    var p = _allPieces[pIdx];
                    bool directFit = p.WidthMm <= maxWidth && p.HeightMm <= maxHeight;
                    bool rotatedFit = allowRotation && p.HeightMm <= maxWidth && p.WidthMm <= maxHeight;

                    if (directFit || rotatedFit)
                    {
                        results.Add((pIdx, p));
                    }
                }
            }

            // 2. Advance starvation cursor
            _queryCursor = (_queryCursor + results.Count + 1) % Math.Max(1, _allPieces.Count);

            return results;
        }

        /// <summary>
        /// Returns all remaining unplaced pieces in order.
        /// </summary>
        public List<CutPiece> GetUnplacedPieces()
        {
            var unplaced = new List<CutPiece>(_remainingCount);
            for (int i = 0; i < _allPieces.Count; i++)
            {
                if (!_placed[i])
                    unplaced.Add(_allPieces[i]);
            }
            return unplaced;
        }
    }
}
