"""How many seconds until the reset time in a Codex budget refusal, printed as one integer.

The refusal is written for a person, not for a script:

    ERROR: You've hit your usage limit. ... or try again at Sep 19th, 2026 7:55 AM.

Its own file rather than a line inside codex-review.sh because the ordinal suffix needs stripping
and every attempt to embed that expression in the shell script lost its escapes in transit.

Prints nothing and exits 1 when the text holds no time it recognises, so the caller can tell the
difference between "no wait needed" and "could not tell".
"""
import datetime
import re
import sys

PATTERN = re.compile(
    r"try again at ([A-Z][a-z]+) (\d+)(?:st|nd|rd|th) (\d{4}) (\d+):(\d+) ([AP]M)")

MONTHS = {
    "Jan": 1, "Feb": 2, "Mar": 3, "Apr": 4, "May": 5, "Jun": 6,
    "Jul": 7, "Aug": 8, "Sep": 9, "Oct": 10, "Nov": 11, "Dec": 12,
}


def seconds_until(text, now=None):
    """Seconds from now until the reset the text names, or None when it names none."""
    # The comma after the day is optional so a caller can pass the date either way.
    match = PATTERN.search(text.replace(",", ""))
    if match is None:
        return None

    month, day, year, hour, minute, meridiem = match.groups()
    if month not in MONTHS:
        return None

    hour = int(hour) % 12
    if meridiem == "PM":
        hour += 12

    when = datetime.datetime(int(year), MONTHS[month], int(day), hour, int(minute))
    return max(0, int((when - (now or datetime.datetime.now())).total_seconds()))


def _self_test():
    """Checked by scripts/verify.sh, because a wait of the wrong length is silent."""
    now = datetime.datetime(2026, 9, 17, 7, 55)
    cases = [
        ("try again at Sep 19th, 2026 7:55 AM", 2 * 24 * 3600),
        ("try again at Sep 17th, 2026 8:55 AM", 3600),
        ("try again at Sep 17th, 2026 12:55 PM", 5 * 3600),
        ("try again at Sep 18th, 2026 12:55 AM", 17 * 3600),   # midnight is hour 0, not 12
        ("try again at Sep 1st, 2026 7:55 AM", 0),             # already past: never negative
        ("no time in here at all", None),
    ]

    for text, expected in cases:
        actual = seconds_until(text, now)
        if actual != expected:
            print(f"FAIL: {text!r} gave {actual}, expected {expected}")
            return 1

    print(f"reset-seconds: {len(cases)} cases pass")
    return 0


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
        sys.exit(_self_test())

    result = seconds_until(sys.stdin.read())
    if result is None:
        sys.exit(1)

    print(result)
