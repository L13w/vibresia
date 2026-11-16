#!/bin/bash
# Is Apple ready to notarize for us yet?
#
# The release cannot go out until Apple's notary service accepts our
# submissions, and it refuses them while the team's Apple Developer Program
# agreement is unsigned or expired:
#
#   HTTP status code: 403. A required agreement is missing or has expired.
#
# That is not something a build script or a stored credential can fix. Only
# the Account Holder can accept it, at developer.apple.com/account (Business
# -> Agreements), and Apple takes a few minutes to notice afterwards.
#
# So this checks the one thing that can be checked from here, and says what
# to do about it if the answer is no.
#
#   ./scripts/check-notary.sh
#
# Exit 0 means notarization can go ahead: the release script can be run.
# Exit 1 means it cannot, and the reason is on the screen.

set -uo pipefail

PROFILE="${NOTARY_PROFILE:-vibresia-notary}"

echo "==> Checking Apple's notary service for the '$PROFILE' profile"

OUTPUT="$(xcrun notarytool history --keychain-profile "$PROFILE" 2>&1)"
if [ $? -eq 0 ]; then
    echo "==> Apple is accepting submissions. The release can go out:"
    echo
    echo "    NOTES=\"What changed\" ./release.sh"
    exit 0
fi

echo "$OUTPUT" >&2
echo >&2

if echo "$OUTPUT" | grep -q "A required agreement is missing or has expired"; then
    cat >&2 <<'EOF'
The team agreement needs accepting. Only the Account Holder can do it:

  1. Sign in at https://developer.apple.com/account as the Account Holder.
  2. Open Business -> Agreements.
  3. Accept anything marked pending or expired.

  If the Agreements tab has nothing pending, the pending item is a revision
  of the Apple Developer Program License Agreement. It appears as a banner
  on the developer account page rather than as a row in the list.

  Apple takes a few minutes to notice afterwards. Run this script again to
  check, and then run the release.
EOF
    exit 1
fi

cat >&2 <<'EOF'
This is not the agreement problem, so the message above is the one to read.
The credentials under the profile may need recreating:

  xcrun notarytool store-credentials <profile> \
      --apple-id "you@minithlabs.com" --team-id TEAMID --password <app-specific>
EOF
exit 1
