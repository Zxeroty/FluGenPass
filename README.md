# FluGenPass

FluGenPass is a modern password manager for Windows (.NET 8) that provides local and secure storage for your credentials. The app adopts the Fluent UI design philosophy with the Mica effect, delivering a native and premium user experience.

## Key Features

- **Fluent Shell**: A modern shell based on `WPF-UI` with navigation through the `Generator`, `Vault`, and `Settings` sections.
- **Password Generator**: 
  - Length customization (8 to 64 characters).
  - Character set control: uppercase/lowercase letters, numbers, and special characters.
  - Entropy-based strength indicator.
  - Quick saving to encrypted storage.
- **Local Storage (Vault)**:
  - Encryption using AES-GCM.
  - Convenient entry management: view, copy, and delete.
  - Import passwords from external sources.
  - Export to CSV (compatible with Bitwarden) and secure backups.
- **Modern design**:
  - Support for the Mica effect and dynamic themes (System, Light, Dark).
  - Intuitive navigation with text hints.
  - Perfect adaptation of interface elements for dark mode.

## Security

- **Argon2id**: The modern Argon2id algorithm (65,536 KB memory, 4 parallel threads) is used to protect the master password. This ensures the highest resistance to brute-force attacks on GPUs and specialized hardware (ASICs).
- **Cryptography**: All passwords are generated using `System.Security.Cryptography.RandomNumberGenerator`.
- **Local Storage**: Your data never leaves your device. The master password is not stored in plain text; only its hash is verified.
- **Session Isolation**: When the vault is locked, the encryption key is immediately removed from RAM.

## Data Storage

All application files are stored locally in the user profile:
`%LocalAppData%\FluGenPass`

- `settings.json`: Theme settings and master password verification metadata.
- `vault.dat`: The encrypted contents of your vault.

## Installation

Currently, the program can be installed using a single .exe file to avoid all the difficulties associated with installing source files and compiling them.
