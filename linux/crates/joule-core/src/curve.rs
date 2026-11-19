//! Voltage to percentage by piecewise-linear curves.

/// Mirrors `LogitechProvider.Interpolate` in the C# app (banker's rounding).
pub fn interpolate(mv: i32, curve: &[(i32, i32)]) -> i32 {
    if mv >= curve[0].0 {
        return curve[0].1;
    }
    for i in 1..curve.len() {
        let (hi_mv, hi_pct) = curve[i - 1];
        let (lo_mv, lo_pct) = curve[i];
        if mv >= lo_mv {
            let v = lo_pct as f64
                + (mv - lo_mv) as f64 * (hi_pct - lo_pct) as f64 / (hi_mv - lo_mv) as f64;
            return v.round_ties_even() as i32;
        }
    }
    curve[curve.len() - 1].1
}

#[cfg(test)]
mod tests {
    use super::*;

    const G633: [(i32, i32); 8] = [
        (4100, 100),
        (3950, 80),
        (3850, 60),
        (3750, 40),
        (3650, 20),
        (3500, 10),
        (3300, 5),
        (3150, 0),
    ];

    #[test]
    fn clamps_above_top() {
        assert_eq!(interpolate(4300, &G633), 100);
    }
    #[test]
    fn exact_points() {
        assert_eq!(interpolate(3950, &G633), 80);
        assert_eq!(interpolate(3500, &G633), 10);
        assert_eq!(interpolate(3150, &G633), 0);
    }
    #[test]
    fn between_points() {
        assert_eq!(interpolate(3900, &G633), 70); // 60 + 50 * 20 / 100
        assert_eq!(interpolate(3488, &G633), 10); // 5 + 188 * 5 / 200 = 9.7
    }
    #[test]
    fn below_bottom_is_last_point() {
        assert_eq!(interpolate(3000, &G633), 0);
    }
}
