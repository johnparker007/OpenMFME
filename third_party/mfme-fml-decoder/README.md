# MFME FML decoder source

Source: https://github.com/paul-maidment/mfme-fml-decoder
Pinned revision: 93101f3fa5792b299f13fc7377c7aac79e0c73b5 (Fixed OffsetX/OffsetY parsing).

These Decoder, Decryption, Model and Utilities C# files are copied from that revision, excluding tests and LayoutExporter. FileWalker, MfmeVersionReader and ComponentWalker contain small, marked OpenMFME changes rejecting record lengths outside the stream before allocating. The CLI, game-configuration exporters and bundled binaries are not included. OpenMFME uses a separate library wrapper and read-only viewer; it does not expose the encoder or repacking functions.

John Parker confirmed on 11 October 2026 that he and Paul wrote this decoder and authorized its use here. The upstream repository has no separate licence file at this revision; this permission does not assign a new licence to the upstream source.

OpenMFME additions live in src/OpenMFME.LayoutDecoder and src/OpenMFME.Desktop. The wrapper applies size, chunk and geometry checks before displaying data, retains the v20.1 version requirement, and serializes decoder calls because upstream diagnostic settings are static.

To update, review an upstream revision, replace the corresponding source files, update the pin here, and rerun decoder fixtures and headless viewer checks.

Tests include five small upstream layout/JSON fixtures (Background, Lamp, Reel, Label and Button), including angled controls. The pinned offset fix adds zero OffsetX/OffsetY background defaults absent from older fixture JSON; checks normalize only those defaults.
