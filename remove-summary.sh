#!/usr/bin/env bash

set -o nounset
set -o pipefail

# Visuals
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${CYAN}>> REMOVING XML DOCUMENTATION COMMENTS FROM PROJECT <<${NC}"

# Check if we're in a git repo or regular directory
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    ROOT=$(git rev-parse --show-toplevel)
    cmd=(git ls-files -z --cached --others --exclude-standard)
else
    ROOT=$(pwd)
    cmd=(find . -type f -not -path '*/.*' -print0)
fi

# Create temp file for processing
TMP_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t 'remove-summary')
trap "rm -rf $TMP_DIR" EXIT

echo -e "${CYAN}Scanning files...${NC}"

# Process files
"${cmd[@]}" | perl -0 -ne '
    BEGIN {
        # Only process C# files
        $cs_re = qr/\.cs$/i;
        
        $modified = 0;
    }

    chomp;
    $f = $_;
    
    # Clean ./ prefix from find
    $f =~ s/^\.\///;

    # Only process .cs files
    next unless ($f =~ $cs_re);
    next unless (-f $f);
    next if (-B $f);

    # Read file content
    if (open(my $fh, "<", $f)) {
        local $/;
        $content = <$fh>;
        close($fh);

        $original = $content;
        
        # Remove entire XML documentation comment blocks (///)
        # This matches any consecutive lines starting with ///
        $content =~ s/^[ \t]*\/\/\/[^\n]*\n([ \t]*\/\/\/[^\n]*\n)*//gm;
        
        # Only write if content changed
        if ($content ne $original) {
            if (open(my $out, ">", $f)) {
                print $out $content;
                close($out);
                print STDERR "MODIFIED: $f\n";
                $modified++;
            }
        }
    }

    END {
        print STDERR "TOTAL_MODIFIED:$modified\n";
    }
' 2> "${TMP_DIR}/log"

# Parse results
MODIFIED_COUNT=$(grep "^TOTAL_MODIFIED:" "${TMP_DIR}/log" | cut -d: -f2)
MODIFIED_FILES=$(grep "^MODIFIED:" "${TMP_DIR}/log" | cut -d: -f2-)

if [ "$MODIFIED_COUNT" -gt 0 ]; then
    echo ""
    echo -e "${GREEN}Modified files:${NC}"
    echo "$MODIFIED_FILES"
    echo ""
    echo -e "${GREEN}✔ Removed XML documentation from ${MODIFIED_COUNT} file(s)${NC}"
else
    echo -e "${YELLOW}No XML documentation comments found in any files.${NC}"
fi