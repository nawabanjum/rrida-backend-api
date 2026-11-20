use strict;
use warnings;

# Removes complete migrationBuilder.<Op>( ... ) statements whose body mentions any
# pre-existing-drift object name. Paren depth is tracked so CreateTable blocks —
# which contain their own semicolons — are matched as one unit.
#
# Drift here means: model changes that were applied to the database by hand-run
# SQL in Repository/Scripts rather than by a migration, so EF believes they are
# still pending and sweeps them into the next migration generated.

my @DRIFT = qw(
    EranCoachConversations EranCoachMessages
    AmountSubtotal DiscountAmount DiscountCode
    ExpiryNotifiedAt ReactivationNotifiedAt
);

my $path = shift or die "usage: strip_drift.pl <migration.cs>\n";

open my $in, '<:encoding(UTF-8)', $path or die "open $path: $!";
my $content = do { local $/; <$in> };
close $in;

my $crlf = ($content =~ /\r\n/) ? 1 : 0;
$content =~ s/\r\n/\n/g;
my @lines = split /\n/, $content, -1;

my @out;
my $i = 0;
my $removed = 0;

while ($i < @lines) {
    if ($lines[$i] !~ /^\s*migrationBuilder\.\w+(?:<[^>]*>)?\(\s*$/) {
        push @out, $lines[$i];
        $i++;
        next;
    }

    my $depth = 0;
    my $j = $i;
    while ($j < @lines) {
        my $l = $lines[$j];
        $depth += ($l =~ tr/(//);
        $depth -= ($l =~ tr/)//);
        last if $depth <= 0 && $j > $i;
        $j++;
    }
    $j = $#lines if $j > $#lines;

    my $block = join "\n", @lines[$i .. $j];
    my $hit = 0;
    for my $d (@DRIFT) {
        if ($block =~ /"\Q$d\E"/) { $hit = 1; last; }
    }

    if ($hit) {
        $removed++;
        my $k = $j + 1;
        $k++ while $k < @lines && $lines[$k] =~ /^\s*$/;
        $i = $k;
    } else {
        push @out, @lines[$i .. $j];
        $i = $j + 1;
    }
}

my $result = join "\n", @out;
$result =~ s/\n/\r\n/g if $crlf;

open my $outfh, '>:raw', $path or die "write $path: $!";
binmode $outfh, ':encoding(UTF-8)';
print $outfh $result;
close $outfh;

print "removed $removed drift statement(s)\n";
