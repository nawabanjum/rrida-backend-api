#!/usr/bin/perl
# One-shot rename helper for the Companies -> Profiles redesign.
#
# Rewrites the CLR identifiers `Company` -> `Profile` and `Companies` -> `Profiles`
# in code only. It deliberately leaves alone:
#   * anything inside a string literal (messages, raw SQL, HTML) - those are handled
#     by hand, because some of them are stored values or shipped API text;
#   * `UserType.Company`, which is persisted to Users.Type as the literal text
#     "Company" and would silently change every future row if renamed;
#   * `// comments`, which are reviewed separately.
use strict;
use warnings;

sub rewrite_code {
    my ($t) = @_;
    $t =~ s/(?<![A-Za-z0-9_])Companies(?![A-Za-z0-9_])/Profiles/g;
    $t =~ s/(?<!UserType\.)(?<![A-Za-z0-9_])Company(?![A-Za-z0-9_])/Profile/g;
    return $t;
}

my $STRING = qr/"(?:[^"\\]|\\.|"")*"/;

for my $file (@ARGV) {
    open my $fh, '<:raw', $file or die "$file: $!";
    my $src = do { local $/; <$fh> };
    close $fh;

    my $out = '';
    for my $line (split /(?<=\n)/, $src) {
        # Split the line into alternating out-of-string / in-string segments, so
        # only code is rewritten.
        my @parts = split /($STRING)/, $line;
        for my $i (0 .. $#parts) {
            next if $i % 2;                      # odd index = string literal
            my $seg = $parts[$i];
            if ($seg =~ m{^(.*?)(//.*)$}s) {     # leave line comments alone
                $parts[$i] = rewrite_code($1) . $2;
            } else {
                $parts[$i] = rewrite_code($seg);
            }
        }
        $out .= join '', @parts;
    }

    next if $out eq $src;
    open my $wfh, '>:raw', $file or die "$file: $!";
    print $wfh $out;
    close $wfh;
    print "rewrote $file\n";
}
