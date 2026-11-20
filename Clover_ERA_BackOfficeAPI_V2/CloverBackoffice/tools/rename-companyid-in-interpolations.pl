#!/usr/bin/perl
# Companion to rename-companyid-to-profileid.pl.
#
# That script skips string literals, which is what protects Stripe metadata
# keys and stored audit JSON. But an interpolated string is part literal and
# part code: in $"CompanyId={companyId}" the label is text and the hole is an
# expression referring to a variable that has just been renamed.
#
# This rewrites identifiers ONLY inside the {...} holes of interpolated
# strings, leaving the surrounding literal text - including log labels like
# "CompanyId=" and licence-key prefixes like "CLD-PRO" - exactly as it was.
use strict;
use warnings;

sub rewrite_hole {
    my ($t) = @_;
    # Only the expression side of a format hole. Anything after a ':' is a
    # format specifier, so stop there.
    my ($expr, $fmt) = $t =~ /^([^:]*)(.*)$/s;
    $expr =~ s/CompanyId/ProfileId/g;
    $expr =~ s/companyId/profileId/g;
    return $expr . $fmt;
}

for my $file (@ARGV) {
    open my $fh, '<:raw', $file or die "$file: $!";
    my $src = do { local $/; <$fh> };
    close $fh;

    my $out = $src;

    # Each interpolated string, then each hole inside it. {{ and }} are escaped
    # braces in an interpolated string and carry no expression.
    $out =~ s{(\$@?"(?:[^"\\]|\\.|"")*")}{
        my $str = $1;
        $str =~ s/(?<!\{)\{([^{}]+)\}/'{' . rewrite_hole($1) . '}'/ge;
        $str;
    }ge;

    next if $out eq $src;
    open my $wfh, '>:raw', $file or die "$file: $!";
    print $wfh $out;
    close $wfh;
    print "rewrote $file\n";
}
