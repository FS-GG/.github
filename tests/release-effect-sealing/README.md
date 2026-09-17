# GS2-08.9 release/publication sealing

Run `python3 tests/release-effect-sealing/run.py`.

The check is offline. It preserves the exact 0.90-bound workflow identities, verifies that the
unbound legacy workflows carry only manual read/local qualification capability, and invokes every
shared saga mutation command with fake credentials and fake provider commands to prove refusal occurs
before an external effect.
