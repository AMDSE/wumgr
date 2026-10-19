#!/bin/sh
set -eu
cd "$(dirname "$0")"
t=$(mktemp .test_policy.XXXXXX); trap 'rm -f "$t"' EXIT INT TERM
gcc -std=c11 -Wall -Wextra -Werror -pedantic test_policy.c -o ./"$t"
./"$t"
