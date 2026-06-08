git filter-branch --msg-filter '
    sed "/^Co-Authored-By:/d"
' --force -- --all
