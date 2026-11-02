# Third-party notices

Guardian's deep scan uses the work of several open projects. Thank you to everyone behind them.

## Bundled with the app

### YARA
The YARA pattern-matching engine (`yara64.exe`, `yarac64.exe`) by VirusTotal, version 4.6.0, is shipped unmodified.
Project: https://github.com/VirusTotal/yara. License: BSD-3-Clause (full text in `LICENSE-YARA.txt` next to the binaries).
`tools/fetch-yara.ps1` downloads it at build time and checks a pinned SHA-256.

## Downloaded onto each user's PC at run time (not redistributed by this project)

### YARA-Forge rule packages
The signature rules come from the "core" package of YARA-Forge (https://github.com/YARAHQ/yara-forge), which merges
rules from many community repositories. **Every rule keeps its own author and license**, recorded in the rule's
metadata and in the package header. Credit goes to all of those authors, including among others
ditekSHen, Florian Roth (Neo23x0), Malpedia / yara-signator, CAPE (kevoreilly), ReversingLabs, Elastic,
Cod3nym, R3c0nst and many more. The package is downloaded from the YARA-Forge GitHub release, verified
against the checksum GitHub publishes for it, and compiled locally.

### LOLDrivers
The list of vulnerable and malicious drivers is from LOLDrivers (https://www.loldrivers.io,
https://github.com/magicsword-io/LOLDrivers). License: Apache-2.0.

### Known-malware hash list
`iocs/hash-iocs.txt` from signature-base by Florian Roth and contributors
(https://github.com/Neo23x0/signature-base), used under the Detection Rule License (DRL) 1.1, which requires
attribution to the rule authors.

## Fonts
Syne, Manrope and Bebas Neue are used under the SIL Open Font License 1.1.
Syne: https://github.com/googlefonts/syne. Manrope: https://github.com/sharanda/manrope. Bebas Neue: https://github.com/dharmatype/Bebas-Neue.
