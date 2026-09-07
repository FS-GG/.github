#!/usr/bin/env python3
"""Executable canary for the prospective reduced routine-development route."""


def selected_route() -> str:
    return "routine"


if __name__ == "__main__":
    assert selected_route() == "routine"
    print("routine route canary: PASS")
