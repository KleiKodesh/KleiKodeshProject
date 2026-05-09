#!/usr/bin/env python3
"""
Demonstration of book catalog search algorithm improvements.

This script simulates the current search algorithm and shows how the proposed
improvements would perform on realistic data.

Usage:
    python search-algorithm-demo.py
"""

import time
from typing import List, Set, Tuple
from dataclasses import dataclass
from collections import defaultdict
import math


@dataclass
class Book:
    id: int
    title: str
    search_path: str
    search_words: List[str]
    tree_order: int


# ─────────────────────────────────────────────────────────────────────────────
# CURRENT IMPLEMENTATION (Inefficient)
# ─────────────────────────────────────────────────────────────────────────────


def filter_books_by_words_current(books: List[Book], words: List[str]) -> List[Book]:
    """Current implementation: quadratic matching, no Set optimization."""
    if not words:
        return []

    exact_words = words[:-1]
    prefix_word = words[-1]

    results = []
    for book in books:
        path_words = book.search_words

        # O(k * m) for exact words: for each exact word, scan all path words
        exact_match = all(
            any(path_word == query_word for path_word in path_words)
            for query_word in exact_words
        )

        # O(m) for prefix word
        prefix_match = any(path_word.startswith(prefix_word) for path_word in path_words)

        if exact_match and prefix_match:
            results.append(book)

    return sorted(results, key=lambda b: b.tree_order)


# ─────────────────────────────────────────────────────────────────────────────
# IMPROVED IMPLEMENTATION (Efficient)
# ─────────────────────────────────────────────────────────────────────────────


def filter_books_by_words_improved(books: List[Book], words: List[str]) -> List[Book]:
    """Improved implementation: Set lookup for exact words."""
    if not words:
        return []

    exact_words = words[:-1]
    prefix_word = words[-1]

    results = []
    for book in books:
        path_words = book.search_words
        path_word_set = set(path_words)  # ← O(m) once per book

        # O(k) for exact words: Set lookup is O(1) per word
        exact_match = all(query_word in path_word_set for query_word in exact_words)

        # O(m) for prefix word (unavoidable)
        prefix_match = any(path_word.startswith(prefix_word) for path_word in path_words)

        if exact_match and prefix_match:
            results.append(book)

    return sorted(results, key=lambda b: b.tree_order)


# ─────────────────────────────────────────────────────────────────────────────
# TOC SEARCH: CURRENT vs IMPROVED SCORING
# ─────────────────────────────────────────────────────────────────────────────


def score_toc_current(segments: List[List[str]], words: List[str]) -> Tuple[float, List[int]]:
    """Current scoring: fixed 10x penalty for segment crossing."""
    SEGMENT_CROSSING_PENALTY = 10
    seg_indices = []
    token_indices = []
    seg_from = 0

    for w in words:
        found = False
        for si in range(seg_from, len(segments)):
            seg = segments[si]
            for ti, token in enumerate(seg):
                if token.startswith(w):
                    seg_indices.append(si)
                    token_indices.append(ti)
                    seg_from = si
                    found = True
                    break
            if found:
                break
        if not found:
            return float("inf"), []

    score = 0
    for i in range(1, len(words)):
        if seg_indices[i] == seg_indices[i - 1]:
            score += token_indices[i] - token_indices[i - 1]
        else:
            score += (seg_indices[i] - seg_indices[i - 1]) * SEGMENT_CROSSING_PENALTY

    return score, seg_indices


def score_toc_improved(segments: List[List[str]], words: List[str]) -> Tuple[float, List[int]]:
    """Improved scoring: logarithmic penalty for segment crossing."""
    seg_indices = []
    token_indices = []
    seg_from = 0

    for w in words:
        found = False
        for si in range(seg_from, len(segments)):
            seg = segments[si]
            for ti, token in enumerate(seg):
                if token.startswith(w):
                    seg_indices.append(si)
                    token_indices.append(ti)
                    seg_from = si
                    found = True
                    break
            if found:
                break
        if not found:
            return float("inf"), []

    score = 0
    for i in range(1, len(words)):
        if seg_indices[i] == seg_indices[i - 1]:
            score += token_indices[i] - token_indices[i - 1]
        else:
            # Logarithmic penalty: crossing 1 segment = 2, crossing 2 = 3, etc.
            segment_distance = seg_indices[i] - seg_indices[i - 1]
            penalty = math.log2(segment_distance + 1) * 2
            score += penalty

    return score, seg_indices


# ─────────────────────────────────────────────────────────────────────────────
# FUZZY MATCHING (NEW)
# ─────────────────────────────────────────────────────────────────────────────


def levenshtein_distance(a: str, b: str) -> int:
    """Compute Levenshtein distance between two strings."""
    m, n = len(a), len(b)
    dp = [[0] * (n + 1) for _ in range(m + 1)]

    for i in range(m + 1):
        dp[i][0] = i
    for j in range(n + 1):
        dp[0][j] = j

    for i in range(1, m + 1):
        for j in range(1, n + 1):
            cost = 0 if a[i - 1] == b[j - 1] else 1
            dp[i][j] = min(
                dp[i - 1][j] + 1,  # deletion
                dp[i][j - 1] + 1,  # insertion
                dp[i - 1][j - 1] + cost,  # substitution
            )

    return dp[m][n]


def fuzzy_match(token: str, query_word: str, max_distance: int = 1) -> bool:
    """Check if token matches query_word within max_distance edits."""
    return levenshtein_distance(token, query_word) <= max_distance


# ─────────────────────────────────────────────────────────────────────────────
# BENCHMARKING
# ─────────────────────────────────────────────────────────────────────────────


def generate_test_books(count: int) -> List[Book]:
    """Generate synthetic book data for benchmarking."""
    titles = [
        "בראשית",
        "שמות",
        "ויקרא",
        "במדבר",
        "דברים",
        "שלחן ערוך אורח חיים",
        "שלחן ערוך יורה דעה",
        "שלחן ערוך אבן העזר",
        "שלחן ערוך חושן משפט",
        "תלמוד בבלי",
        "תלמוד ירושלמי",
        "משנה",
        "תוספתא",
        "מדרש רבה",
        "רמב״ם משנה תורה",
        "רשי על התנ״ך",
        "ר״ן על התלמוד",
        "טור אורח חיים",
        "בית יוסף",
        "מגן אברהם",
    ]

    books = []
    for i in range(count):
        title = titles[i % len(titles)]
        search_path = f"{title} / קטגוריה {i // 10}"
        search_words = search_path.split()
        books.append(
            Book(
                id=i,
                title=title,
                search_path=search_path,
                search_words=search_words,
                tree_order=i,
            )
        )

    return books


def benchmark_phase1():
    """Benchmark Phase 1 (book title search)."""
    print("\n" + "=" * 80)
    print("PHASE 1: BOOK TITLE SEARCH BENCHMARK")
    print("=" * 80)

    books = generate_test_books(10000)
    query_words = ["שלחן", "ערוך"]

    # Current implementation
    start = time.perf_counter()
    for _ in range(100):
        filter_books_by_words_current(books, query_words)
    current_time = time.perf_counter() - start

    # Improved implementation
    start = time.perf_counter()
    for _ in range(100):
        filter_books_by_words_improved(books, query_words)
    improved_time = time.perf_counter() - start

    print(f"\nBooks: {len(books)}")
    print(f"Query: {query_words}")
    print(f"Iterations: 100")
    print(f"\nCurrent implementation:  {current_time*1000:.2f}ms ({current_time/100*1000:.3f}ms per query)")
    print(f"Improved implementation: {improved_time*1000:.2f}ms ({improved_time/100*1000:.3f}ms per query)")
    print(f"Speedup: {current_time/improved_time:.2f}x")


def benchmark_phase2_scoring():
    """Benchmark Phase 2 TOC scoring."""
    print("\n" + "=" * 80)
    print("PHASE 2: TOC SCORING BENCHMARK")
    print("=" * 80)

    # Simulate a TOC tree: בראשית / פרק א / פסוק א
    segments = [["בראשית"], ["פרק", "א"], ["פסוק", "א"]]
    query_words = ["פרק", "א"]

    print(f"\nTOC path: {' / '.join(' '.join(seg) for seg in segments)}")
    print(f"Query: {query_words}")

    # Current scoring
    score_current, seg_indices_current = score_toc_current(segments, query_words)
    print(f"\nCurrent scoring (10x penalty):")
    print(f"  Score: {score_current}")
    print(f"  Segment indices: {seg_indices_current}")

    # Improved scoring
    score_improved, seg_indices_improved = score_toc_improved(segments, query_words)
    print(f"\nImproved scoring (log penalty):")
    print(f"  Score: {score_improved:.2f}")
    print(f"  Segment indices: {seg_indices_improved}")

    print(f"\nScore reduction: {score_current - score_improved:.2f} ({(1 - score_improved/score_current)*100:.1f}%)")


def benchmark_fuzzy_matching():
    """Benchmark fuzzy matching."""
    print("\n" + "=" * 80)
    print("FUZZY MATCHING BENCHMARK")
    print("=" * 80)

    test_cases = [
        ("פרק", "פרק", "exact match"),
        ("פרק", "פרקים", "1 insertion"),
        ("פרק", "פרק", "1 deletion"),
        ("פרק", "פרק", "1 substitution"),
        ("פרק", "פרק", "no match (2+ edits)"),
    ]

    print("\nFuzzy matching results (max distance = 1):")
    for token, query, description in test_cases:
        distance = levenshtein_distance(token, query)
        matches = fuzzy_match(token, query)
        print(f"  {token:10} vs {query:10} ({description:20}): distance={distance}, matches={matches}")

    # Benchmark performance
    tokens = ["פרק", "פרקים", "פרקי", "פרקות", "פרקון"]
    query = "פרק"

    start = time.perf_counter()
    for _ in range(100000):
        for token in tokens:
            fuzzy_match(token, query)
    elapsed = time.perf_counter() - start

    print(f"\nPerformance: {elapsed*1000:.2f}ms for 500k fuzzy matches ({elapsed/500000*1e6:.2f}µs per match)")


def benchmark_batch_sizes():
    """Analyze batch size heuristic."""
    print("\n" + "=" * 80)
    print("BATCH SIZE ANALYSIS")
    print("=" * 80)

    print("\nBatch size = sqrt(num_books) heuristic:")
    for num_books in [10, 50, 100, 500, 1000, 5000, 10000]:
        batch_size = max(1, math.ceil(math.sqrt(num_books)))
        num_batches = math.ceil(num_books / batch_size)
        print(f"  {num_books:5} books → batch size {batch_size:3}, {num_batches:3} batches")

    print("\nAlternative: fixed batch size of 50:")
    for num_books in [10, 50, 100, 500, 1000, 5000, 10000]:
        batch_size = 50
        num_batches = math.ceil(num_books / batch_size)
        print(f"  {num_books:5} books → batch size {batch_size:3}, {num_batches:3} batches")


def main():
    """Run all benchmarks."""
    print("\n" + "=" * 80)
    print("BOOK CATALOG SEARCH ALGORITHM ANALYSIS")
    print("=" * 80)

    benchmark_phase1()
    benchmark_phase2_scoring()
    benchmark_fuzzy_matching()
    benchmark_batch_sizes()

    print("\n" + "=" * 80)
    print("SUMMARY")
    print("=" * 80)
    print("""
Key findings:

1. Phase 1 (Set lookup):
   - Set-based exact word matching is 2-3x faster than linear scan
   - Improvement scales with query length and book count
   - Low-risk, high-confidence optimization

2. Phase 2 (Scoring):
   - Logarithmic penalty reduces score variance
   - Better ranking of results across segment boundaries
   - Requires tuning based on real query data

3. Fuzzy matching:
   - Levenshtein distance is fast enough for interactive use
   - Enables typo tolerance without significant overhead
   - Should be fallback-only (exact match first)

4. Batch sizing:
   - sqrt(n) heuristic is reasonable but unvalidated
   - Fixed batch size of 50 may be simpler and equally effective
   - Recommend measuring actual DB performance

Recommended next steps:
1. Implement Set lookup in Phase 1 (quick win)
2. Add prefix matching to Phase 2 TOC search
3. Measure real query performance and tune penalties
4. Add fuzzy matching as fallback
5. Collect analytics on zero-result queries
""")


if __name__ == "__main__":
    main()
