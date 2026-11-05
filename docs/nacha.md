# Fictional ACH format contract

Supported subset: ASCII, 94 characters per record, LF delimiters, ten-record blocks padded with all-9 records; one unbalanced PPD debit batch (service class 225), transaction code 27, no addenda or offset entry. Record types 1/5/6/8/9 carry file, batch, entry and control information. Debit amounts are integer USD cents. Credits are zero. Routing checksum and batch/file entry counts, routing hash modulo 10^10, debit totals and block count are validated.

Stable ordering is by invoice identity. Effective date is supplied explicitly; file creation date uses that date and time 0000 so exports are deterministic. Trace numbers restart at one for the single batch. File ID modifier A and fictional origin 1123456789 are fixed. Routing 091000019 is checksum-valid but used only as a format fixture; account strings DEMOACCOUNT0001 etc. are deliberately fictional. No financial authorization or account ownership is implied.

The validator supports only this narrow subset; it is not a general NACHA validator. It does not validate bank enrollment, production origin/destination assignments, cutoff/holiday rules, duplicate submission windows, prenotes, sanctions, authorizations, reversals or bank-specific envelopes. Reconciliation uses payment IDs in a synthetic JSON result, not a bank return parser. Final settled/returned outcomes are immutable; equal retries are no-ops and conflicting retries fail atomically.

**Do not submit these files. Not certified for live ACH processing; bank-specific validation, compliance and security review are required.**
