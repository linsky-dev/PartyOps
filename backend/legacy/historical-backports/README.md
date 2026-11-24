# Historical backport source recovery (not current acceptance)

Recovered byte-for-byte Git blobs from PartyOps commit 21bbe6e26b7dc161f2ac495b3bcf84fc8ed9b1bd. These are historical patches and evidence, not new RC1 test results. Do not feed this directory to the production validator as fresh evidence. Original wheel hashes remain mandatory; do not rename or rehash a rebuilt wheel to bypass the lock. The current RC1 lock retains matching hashes for ten of eleven private package names in both architectures. The python-calamine private patch was not located in this historical tree.

The audit workflow independently downloads upstream sdists over HTTPS, checks the supplied published digest, compares the stronger historical pin where present, validates patch application, and attempts candidate pure-Python wheel builds. Any mismatch remains unresolved; no installer or production lock change is authorized.
