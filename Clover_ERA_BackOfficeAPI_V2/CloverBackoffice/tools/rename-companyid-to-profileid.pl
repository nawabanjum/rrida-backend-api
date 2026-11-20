#!/usr/bin/perl
# One-shot rename helper for the second half of the Companies -> Profiles
# redesign: the foreign key column name itself.
#
# Rewrites the identifiers CompanyId / companyId / companyid -> ProfileId /
# profileId / profileid in code and in comments.
#
# It deliberately leaves STRING LITERALS alone, which is what keeps three
# categories of persisted data intact:
#
#   * Stripe metadata keys - `Metadata["companyId"]` is stored on Stripe's own
#     servers against existing customers, sessions and subscriptions. Renaming
#     the key we write would make TryGetValue fail on every object created
#     before the change, breaking webhook handling for existing customers.
#   * AuditLogs / insight content JSON - `content["companyId"]` is written into
#     stored rows and read back later by the dashboard. Renaming the key would
#     orphan every historical row.
#   * `UserType.Company` and the payment `Scope` value "Company" - both are
#     persisted as literal text.
#
# CompanyTeam.cs is excluded by the caller: that table is dead (0 rows) and is
# being left exactly as it is.
use strict;
use warnings;

sub rewrite {
    my ($t) = @_;
    $t =~ s/CompanyId/ProfileId/g;
    $t =~ s/companyId/profileId/g;
    $t =~ s/companyid/profileid/g;
    return $t;
}

my $STRING = qr/"(?:[^"\\]|\\.|"")*"/;

for my $file (@ARGV) {
    open my $fh, '<:raw', $file or die "$file: $!";
    my $src = do { local $/; <$fh> };
    close $fh;

    my $out = '';
    for my $line (split /(?<=\n)/, $src) {
        # Split into alternating code / string-literal segments so only code is
        # rewritten. A // comment is rewritten whole, quotes included, so that
        # <param name="companyId"> in an XML doc comment tracks the parameter it
        # documents.
        my @parts = split /($STRING)/, $line;
        my $done = '';
        my $in_comment = 0;

        for my $i (0 .. $#parts) {
            my $seg = $parts[$i];

            if ($in_comment) {
                $done .= rewrite($seg);
                next;
            }

            if ($i % 2) {           # string literal
                $done .= $seg;
                next;
            }

            if ($seg =~ m{^(.*?)(//.*)$}s) {
                $done .= rewrite($1) . rewrite($2);
                $in_comment = 1;
            } else {
                $done .= rewrite($seg);
            }
        }

        $out .= $done;
    }

    next if $out eq $src;
    open my $wfh, '>:raw', $file or die "$file: $!";
    print $wfh $out;
    close $wfh;
    print "rewrote $file\n";
}
