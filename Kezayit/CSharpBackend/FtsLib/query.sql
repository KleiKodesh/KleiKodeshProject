SELECT term, length(term) - 6 as prefixlen FROM term_index WHERE term LIKE '%ישראל' ORDER BY prefixlen, term;
